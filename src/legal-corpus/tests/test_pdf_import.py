from app.services.pdf import read_pdf

HEADER = "N° 58 Journal Officiel de la République Tunisienne — 15 juillet 2016 Page {n}"


def _escape(line: str) -> str:
    return line.replace("—", "-").replace("(", r"\(").replace(")", r"\)")


def make_pdf(pages: list[list[str]]) -> bytes:
    """A minimal text PDF, one line per text row, so the tests need no PDF-writing library."""
    objects = ["<< /Type /Catalog /Pages 2 0 R >>", None, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"]
    kids = []
    for lines in pages:
        rows = "".join(
            f"BT /F1 10 Tf 40 {800 - 14 * i} Td ({_escape(line)}) Tj ET\n"
            for i, line in enumerate(lines)
        ).encode("cp1252")
        objects.append(f"<< /Length {len(rows)} >>\nstream\n".encode("latin-1") + rows + b"endstream")
        content = len(objects)
        objects.append(f"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents {content} 0 R /Resources << /Font << /F1 3 0 R >> >> >>")
        kids.append(len(objects))
    objects[1] = f"<< /Type /Pages /Kids [{' '.join(f'{k} 0 R' for k in kids)}] /Count {len(kids)} >>"

    out = bytearray(b"%PDF-1.4\n")
    offsets = []
    for number, body in enumerate(objects, start=1):
        offsets.append(len(out))
        out += f"{number} 0 obj\n".encode() + (body if isinstance(body, bytes) else body.encode("latin-1")) + b"\nendobj\n"
    xref = len(out)
    out += f"xref\n0 {len(objects) + 1}\n0000000000 65535 f \n".encode()
    out += "".join(f"{o:010d} 00000 n \n" for o in offsets).encode()
    out += f"trailer\n<< /Size {len(objects) + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n".encode()
    return bytes(out)


LAW_PAGES = [
    [HEADER.format(n=1), "Loi n° 2016-48 du 11 juillet 2016, relative aux", "banques et aux établissements financiers (1).",
     "Au nom du peuple,", "Article premier - La présente loi a pour objectif d'organiser les", "opérations bancaires.",
     "Art. 2 - Les dispositions de la présente loi s'appliquent aux", "banques exerçant en Tunisie."],
    [HEADER.format(n=2), "Art. 3 - Le secret professionnel s'impose à toute personne.",
     "Arrêté du ministre des finances du 1 er mars 2016, portant", "création d'un bureau des douanes.",
     "Article premier - Il est créé un bureau des douanes.", "Art. 2 - Le présent arrêté sera publié."],
]


def test_reads_law_and_drops_gazette_headers():
    law = read_pdf(make_pdf(LAW_PAGES))
    assert law.pages == 2 and len(law.candidates) == 2

    loi, arrete = law.candidates
    assert loi.reference == "Loi n° 2016-48 du 11 juillet 2016"
    assert loi.title == "Loi n° 2016-48 du 11 juillet 2016, relative aux banques et aux établissements financiers"
    assert loi.article_count == 3 and "Journal Officiel" not in loi.raw_text
    assert arrete.reference == "Arrêté du ministre des finances du 1er mars 2016" and arrete.article_count == 2


def test_scan_without_text_is_refused(client):
    response = client.post("/import-pdf", files={"file": ("scan.pdf", make_pdf([[""], [""]]), "application/pdf")},
                           headers={"referer": "http://testserver/import-pdf"}, follow_redirects=False)
    assert response.status_code == 303 and "flash=err_pdf_scanned" in response.headers["location"]


def test_not_a_pdf_is_refused(client):
    response = client.post("/import-pdf", files={"file": ("x.pdf", b"hello", "application/pdf")},
                           headers={"referer": "http://testserver/import-pdf"}, follow_redirects=False)
    assert "flash=err_pdf_unreadable" in response.headers["location"]


def test_upload_choose_review_create(client):
    page = client.post("/import-pdf", files={"file": ("jort.pdf", make_pdf(LAW_PAGES), "application/pdf")},
                       headers={"accept-language": "fr"}).text
    assert "Ce PDF contient plusieurs textes" in page and "3 article(s)" in page

    law = read_pdf(make_pdf(LAW_PAGES)).candidates[0]
    review = client.post("/import-pdf/review", data={"raw_text": law.raw_text, "title": law.title,
                                                     "reference": law.reference, "filename": "jort.pdf"}).text
    assert "3 article(s) found" in review and "Loi n° 2016-48 du 11 juillet 2016" in review

    response = client.post("/import-pdf/create", data={
        "raw_text": law.raw_text, "title": law.title, "reference": law.reference, "jurisdiction": "Tunisia",
        "category": "Commercial", "language": "fr", "filename": "jort.pdf",
    }, follow_redirects=False)
    assert response.status_code == 303 and "flash=pdf_imported" in response.headers["location"]

    text = client.get("/api/v1/texts", params={"q": "2016-48"}).json()
    text = text[0] if isinstance(text, list) else text["items"][0]
    assert text["verified_by"] is None and text["status"] == "Draft"


def test_create_with_repeated_numbers_keeps_the_review(client):
    raw = "Article 1 - Premier texte de loi assez long.\nArticle 1 - Le même numéro répété ici."
    page = client.post("/import-pdf/create", data={"raw_text": raw, "title": "Loi test", "jurisdiction": "Tunisia",
                                                   "category": "Other", "language": "fr"}).text
    assert "Some article numbers repeat" in page and "Le même numéro répété ici." in page


def test_scraps_are_dropped_and_split_numbers_mended():
    pages = [[HEADER.format(n=1), "lois de finances et autres textes.",
              "Loi organique n° 2015 -26 du 7 août 2015, relative à la lutte contre", "le blanchiment d'argent (1).",
              "Article premier - La présente loi vise à prévenir le blanchiment d'argent.",
              "Art. 2 - Les dispositions de la présente loi s'appliquent à toute personne."]]
    [law] = read_pdf(make_pdf(pages)).candidates
    assert law.reference == "Loi organique n° 2015-26 du 7 août 2015" and law.article_count == 2


def test_a_single_real_text_goes_straight_to_review(client):
    pages = [[HEADER.format(n=1), "lois de finances et autres textes.",
              "Loi n° 2099-5 du 1er mars 2099, relative aux essais.",
              "Article premier - La présente loi vise à tester l'import direct des textes.",
              "Art. 2 - Les dispositions de la présente loi s'appliquent à tous."]]
    page = client.post("/import-pdf", files={"file": ("x.pdf", make_pdf(pages), "application/pdf")}).text
    assert "2 article(s) found" in page
