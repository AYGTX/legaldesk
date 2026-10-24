import io

import pytest

from pypdf import PdfReader, PdfWriter
from pypdf.generic import ArrayObject, DictionaryObject, FloatObject, NameObject, NumberObject, TextStringObject

from app.services import formdetect
from app.services.llm import LlmUnavailable

PAGE_H = 842


@pytest.fixture(autouse=True)
def _no_installed_model(monkeypatch):
    """CommonForms may be installed here; tests must never run it or download its weights."""
    monkeypatch.setattr(formdetect, "load_model", lambda: None)


def _pdf_with_text(lines: list[tuple[float, float, str]]) -> bytes:
    """One A4 page; each line is (x, y from the bottom, text)."""
    content = "".join(f"BT /F1 11 Tf {x} {y} Td ({t}) Tj ET\n" for x, y, t in lines).encode("latin-1")
    objs = [b"<< /Type /Catalog /Pages 2 0 R >>", b"<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R "
            b"/Resources << /Font << /F1 5 0 R >> >> >>",
            b"<< /Length %d >>\nstream\n" % len(content) + content + b"endstream",
            b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"]
    out, offsets = bytearray(b"%PDF-1.4\n"), []
    for i, body in enumerate(objs, start=1):
        offsets.append(len(out))
        out += b"%d 0 obj\n" % i + body + b"\nendobj\n"
    start = len(out)
    out += b"xref\n0 %d\n0000000000 65535 f \n" % (len(objs) + 1)
    for off in offsets:
        out += b"%010d 00000 n \n" % off
    out += b"trailer\n<< /Size %d /Root 1 0 R >>\nstartxref\n%d\n%%%%EOF" % (len(objs) + 1, start)
    return bytes(out)


def _add_widgets(pdf: bytes, widgets: list[tuple[str, str, tuple[float, float, float, float]]]) -> bytes:
    """widgets: (field type '/Tx' '/Btn' '/Sig', name, rect in PDF points from the bottom-left)."""
    writer = PdfWriter(clone_from=PdfReader(io.BytesIO(pdf)))
    refs = ArrayObject()
    for ft, name, rect in widgets:
        annot = DictionaryObject({
            NameObject("/Type"): NameObject("/Annot"), NameObject("/Subtype"): NameObject("/Widget"),
            NameObject("/FT"): NameObject(ft), NameObject("/T"): TextStringObject(name),
            NameObject("/Rect"): ArrayObject(FloatObject(v) for v in rect), NameObject("/F"): NumberObject(4)})
        refs.append(writer._add_object(annot))
    writer.pages[0][NameObject("/Annots")] = refs
    out = io.BytesIO()
    writer.write(out)
    return out.getvalue()


def _contract() -> bytes:
    pdf = _pdf_with_text([(50, 760, "Nom du client :"), (50, 720, "Date de naissance"), (50, 680, "Montant (TND) :"),
                          (50, 640, "Signature du client")])
    return _add_widgets(pdf, [("/Tx", "f1", (160, 755, 360, 773)), ("/Tx", "f2", (50, 695, 250, 713)),
                              ("/Tx", "f3", (160, 675, 360, 693)), ("/Sig", "f4", (50, 590, 250, 635)),
                              ("/Btn", "f5", (400, 755, 412, 767))])


def test_existing_fields_get_labels_types_and_coordinates():
    result = formdetect.detect(_contract(), prepare_form=_no_model)
    by_label = {f.label: f for f in result.fields}
    assert result.pages == 1
    assert set(by_label) >= {"Nom du client", "Date de naissance", "Montant (TND)", "Signature du client"}
    assert by_label["Nom du client"].type == "Text"
    assert by_label["Nom du client"].auto_fill_from is None  # the client is the other party, not the requester
    assert by_label["Date de naissance"].type == "Date"  # label printed above the box
    assert by_label["Montant (TND)"].type == "Number"
    sig = by_label["Signature du client"]
    assert (sig.type, sig.fill_role) == ("Signature", "Approver")
    # top-left origin, pixels at 1.5x: the name box starts at x=160pt and y = 842-773 = 69pt from the top
    name = by_label["Nom du client"]
    assert (name.x, name.y) == (240.0, 103.5)
    assert (name.width, name.height) == (300.0, 27.0)
    assert len({f.field_key for f in result.fields}) == len(result.fields)


def test_model_fields_are_added_only_where_the_pdf_has_none():
    base = _pdf_with_text([(50, 700, "Fait le")])

    def fake_model(src, dst, **kw):  # finds one blank next to "Fait le", plus one that duplicates an existing field
        open(dst, "wb").write(_add_widgets(base, [("/Tx", "m1", (110, 695, 260, 713))]))

    existing = _add_widgets(base, [("/Tx", "own", (300, 695, 400, 713))])
    result = formdetect.detect(existing, prepare_form=fake_model)
    assert result.model == "commonforms"
    assert sorted(f.source for f in result.fields) == ["model", "pdf"]
    done = next(f for f in result.fields if f.source == "model")
    assert (done.type, done.fill_role, done.auto_fill_from) == ("Date", "System", "system.today")


def test_model_failure_keeps_the_pdf_fields_and_says_so():
    def broken(src, dst, **kw):
        raise RuntimeError("no weights")

    result = formdetect.detect(_contract(), prepare_form=broken)
    assert len(result.fields) == 5
    assert result.notes and result.notes[0].startswith("model_failed")


class _Llm:
    def __init__(self, name, answer):
        self.name, self.answer = name, answer

    def complete_json(self, system, user):
        if isinstance(self.answer, Exception):
            raise self.answer
        return self.answer


def test_language_model_runs_only_locally_and_cannot_change_a_signature():
    plain = formdetect.detect(_contract(), prepare_form=_no_model).fields
    sig_index = next(i for i, f in enumerate(plain) if f.type == "Signature")
    answer = {"fields": [{"index": 0, "label": "Nom complet du client", "type": "Text"},
                         {"index": sig_index, "label": "Paraphe", "type": "Text"}]}
    remote = formdetect.detect(_contract(), _Llm("groq", answer), prepare_form=_no_model)
    assert remote.ai_used is False and remote.fields[0].label == plain[0].label

    local = formdetect.detect(_contract(), _Llm("local", answer), prepare_form=_no_model)
    assert local.ai_used is True
    assert local.fields[0].label == "Nom complet du client" and local.fields[0].confidence == "ai"
    signature = local.fields[sig_index]
    assert signature.type == "Signature"
    assert signature.label == "Paraphe"  # relabelled but still a signature


def _no_model(src, dst, **kw):
    open(dst, "wb").write(open(src, "rb").read())


def test_unavailable_language_model_falls_back_to_rules():
    result = formdetect.detect(_contract(), _Llm("local", LlmUnavailable("down")), prepare_form=_no_model)
    assert result.ai_used is False and any(n.startswith("ai_unavailable") for n in result.notes)
    assert len(result.fields) == 5


def test_api_detects_fields(client):
    response = client.post("/api/v1/forms/detect", files={"file": ("c.pdf", _contract(), "application/pdf")})
    assert response.status_code == 200, response.text
    body = response.json()
    assert body["pages"] == 1 and len(body["fields"]) >= 5
    assert {"Text", "Signature", "Checkbox"} <= {f["type"] for f in body["fields"]}


def test_api_refuses_non_pdf(client):
    response = client.post("/api/v1/forms/detect", files={"file": ("c.pdf", b"hello", "application/pdf")})
    assert response.status_code == 422


def test_label_stops_at_the_neighbouring_field_and_checkbox_reads_the_text_after_it():
    pdf = _pdf_with_text([(50, 700, "Nom :"), (330, 700, "Prenom :"), (50, 650, "Approuve")])
    pdf = _add_widgets(pdf, [("/Tx", "a", (100, 695, 250, 713)), ("/Tx", "b", (400, 695, 550, 713)),
                             ("/Btn", "c", (35, 648, 47, 660))])
    labels = {f.type: f.label for f in formdetect.detect(pdf, prepare_form=_no_model).fields}
    texts = [f.label for f in formdetect.detect(pdf, prepare_form=_no_model).fields if f.type == "Text"]
    assert texts == ["Nom", "Prenom"]  # "Nom" is not glued onto the second field's label
    assert labels["Checkbox"] == "Approuve"


def test_internal_widget_names_never_become_labels():
    pdf = _add_widgets(_pdf_with_text([]), [("/Tx", "textbox_0_6", (100, 695, 250, 713)),
                                           ("/Tx", "undefined", (100, 645, 250, 663))])
    labels = [f.label for f in formdetect.detect(pdf, prepare_form=_no_model).fields]
    assert labels == ["Field 1", "Field 2"]


def test_model_is_called_with_the_working_model_and_signature_fields():
    calls = []

    def spy(src, dst, **kw):
        calls.append(kw)
        open(dst, "wb").write(open(src, "rb").read())

    formdetect.detect(_contract(), prepare_form=spy)
    assert calls == [{"model_or_path": "FFDNet-L", "use_signature_fields": True}]
