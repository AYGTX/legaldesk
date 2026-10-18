"""Optional language model for Harvey, behind the same switch LegalDesk uses: Groq, a local Ollama model, or none.

Only law text is ever sent to it — public texts, never a contract — and what it returns is a proposal that a person
reviews before anything is saved. With the provider set to "none" (the default) Harvey works exactly as before."""

import json
import urllib.error
import urllib.request
from dataclasses import dataclass
from typing import Protocol


class LlmUnavailable(Exception):
    """No provider configured, or the provider could not be reached or answered something unusable."""


class LlmProvider(Protocol):
    name: str

    def complete_json(self, system: str, user: str) -> dict: ...


@dataclass
class NoLlm:
    name: str = "none"

    def complete_json(self, system: str, user: str) -> dict:
        raise LlmUnavailable("No language model is configured (CORPUS_AI_PROVIDER=none).")


def _post_json(url: str, body: dict, headers: dict[str, str], timeout: float) -> dict:
    request = urllib.request.Request(url, data=json.dumps(body).encode("utf-8"), method="POST",
                                     headers={"Content-Type": "application/json", **headers})
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            return json.loads(response.read().decode("utf-8"))
    except (urllib.error.URLError, TimeoutError, json.JSONDecodeError) as error:
        raise LlmUnavailable(f"The language model could not be reached: {error}") from error


def _parse(content: str) -> dict:
    try:
        data = json.loads(content)
    except (TypeError, json.JSONDecodeError) as error:
        raise LlmUnavailable("The language model did not return JSON.") from error
    if not isinstance(data, dict):
        raise LlmUnavailable("The language model did not return a JSON object.")
    return data


@dataclass
class GroqLlm:
    api_key: str
    model: str
    timeout: float = 120
    name: str = "groq"

    def complete_json(self, system: str, user: str) -> dict:
        body = {
            "model": self.model,
            "temperature": 0,
            "response_format": {"type": "json_object"},
            "messages": [{"role": "system", "content": system}, {"role": "user", "content": user}],
        }
        answer = _post_json("https://api.groq.com/openai/v1/chat/completions", body,
                            {"Authorization": f"Bearer {self.api_key}"}, self.timeout)
        try:
            return _parse(answer["choices"][0]["message"]["content"])
        except (KeyError, IndexError) as error:
            raise LlmUnavailable("Unexpected answer from Groq.") from error


@dataclass
class LocalLlm:
    base_url: str
    model: str
    timeout: float = 600
    name: str = "local"

    def complete_json(self, system: str, user: str) -> dict:
        body = {
            "model": self.model,
            "stream": False,
            "format": "json",
            "options": {"temperature": 0},
            "messages": [{"role": "system", "content": system}, {"role": "user", "content": user}],
        }
        answer = _post_json(f"{self.base_url.rstrip('/')}/api/chat", body, {}, self.timeout)
        try:
            return _parse(answer["message"]["content"])
        except KeyError as error:
            raise LlmUnavailable("Unexpected answer from the local model.") from error


def make_provider(settings) -> LlmProvider:
    provider = (settings.ai_provider or "none").lower()
    if provider == "groq" and settings.groq_api_key:
        return GroqLlm(settings.groq_api_key, settings.groq_model)
    if provider == "local":
        return LocalLlm(settings.local_llm_url, settings.local_llm_model)
    return NoLlm()
