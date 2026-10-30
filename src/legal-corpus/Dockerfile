# Harvey: verified law corpus, risk grid and contract review (rework spec M11).
FROM python:3.11-slim AS base
ENV PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1 PIP_NO_CACHE_DIR=1 \
    HF_HOME=/opt/hf TRANSFORMERS_OFFLINE=0
WORKDIR /srv/harvey

# CPU-only PyTorch keeps the image small; then the service, the meaning model and the MCP server.
COPY requirements*.txt ./
RUN pip install torch --index-url https://download.pytorch.org/whl/cpu \
 && pip install -r requirements.txt -r requirements-semantic.txt -r requirements-mcp.txt

# The meaning model is baked into the image: a server runs Harvey with no internet access.
RUN python -c "from sentence_transformers import SentenceTransformer; SentenceTransformer('sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2')"
ENV TRANSFORMERS_OFFLINE=1 HF_HUB_OFFLINE=1

COPY app ./app
RUN useradd --system --uid 10001 harvey && mkdir -p /srv/harvey/data && chown -R harvey /srv/harvey
USER harvey

# The corpus database and secrets live in the mounted data volume (CORPUS_DATABASE_URL may point elsewhere).
VOLUME ["/srv/harvey/data"]
EXPOSE 8100
HEALTHCHECK --interval=30s --timeout=5s --start-period=60s CMD python -c "import urllib.request; urllib.request.urlopen('http://127.0.0.1:8100/health', timeout=4)" || exit 1
CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8100"]
