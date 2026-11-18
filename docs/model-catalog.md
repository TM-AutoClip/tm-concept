# The repository's model catalog (`catalog/models.json`)

Since 0.1.26 a release that only adds models needs no app update: the app reads `catalog/models.json` of this repository from
`https://raw.githubusercontent.com/TM-AutoClip/tm-concept/main/catalog/models.json` (that exact address, HTTPS, no redirect to another
host) at most once a day, in the background after the main window opened, and keeps the last good copy in `<data folder>/catalog/`. At
the next start the kept copy is checked again and its models are added before anything reads the model lists. Nothing ever waits for the
network, and the start never fails because of the file.

## What it may hold

```json
{
  "schema": 1,
  "date": "2026-11-18",
  "minApp": "0.1.26",
  "providers": [
    { "provider": "openai", "id": "…", "name": "…", "capabilities": "Chat, Schema", "contextTokens": 400000, "maxOutputTokens": 128000,
      "efforts": ["low", "medium"], "inputUsd": 1.25, "outputUsd": 10, "cacheReadUsd": 0, "imageUsd": 0, "speechUsdPerMillionCharacters": 0,
      "retiresOn": "" }
  ],
  "images": [
    { "engine": "sd-cpp", "id": "…", "name": "…", "family": "z-image", "steps": 8, "cfgScale": 1, "sampler": "euler", "scheduler": null,
      "flowShift": null, "maxPixels": 0, "negativePrompt": "", "license": "apache-2.0", "licenseUse": "Commercial", "licenseUrl": "",
      "tier": 6, "minSdCppBuild": 0,
      "files": [
        { "role": "Diffusion", "host": "huggingface.co", "repository": "owner/name", "commit": "<40 hex>", "path": "file.gguf",
          "sha256": "<64 hex>", "bytes": 123 }
      ] }
  ],
  "ollama": [
    { "tag": "name:exact-tag", "name": "…", "family": "name", "bytes": 123, "contextTokens": 8192, "trainedContextTokens": 262144,
      "kvBytesPerToken": 32768, "vision": true, "canThink": true, "license": "apache-2.0", "licenseUse": "Commercial", "licenseUrl": "",
      "sourceUrl": "https://ollama.com/library/name/tags", "manifestDigest": "sha256:<64 hex>" }
  ]
}
```

- **providers**: models of cloud providers the app already has (a provider of the built-in catalog), only with capabilities and reasoning
  efforts that provider's built-in models have. Their prices are the fallback; `pricing/prices.json` still updates them.
- **images**: local image models for stable-diffusion.cpp (`engine` must be `sd-cpp`) with a family, sampler and scheduler a built-in
  model uses, and only families a built-in model runs without extra sd-cli options (SD 3.5 and Qwen-Image 2.1 need theirs, so a model of
  those families comes with an app update). There is no field for command-line arguments. Every file: `host` `huggingface.co` (it is in
  the download allow-list), a 40-hex commit (never a branch), a path inside the repository, its SHA-256 and size; the download is checked
  against them like every built-in model. A model whose `minSdCppBuild` is newer than the oldest stable-diffusion.cpp build the apps pin
  (1043) is left out until an app update.
- **ollama**: models for the app's own Ollama copy, an exact tag (never `latest`) and `manifestDigest`: `sha256:` followed by the digest
  `/api/tags` lists for the tag after a pull (the SHA-256 of the tag's manifest file in the models folder). Ollama checks every layer's SHA-256 itself; after the pull the app checks
  the manifest and removes the model when it differs.
- Voices and the OCR model are code in the app (each voice is its own engine), so they are not in this file.
- Licences are required; a model whose licence is not free for commercial use needs `licenseUrl` and is asked about before its download,
  like a built-in one.

## What makes the app refuse the file

The whole file is refused (one line in the log; the built-in catalog and the last good copy stay) when it is not valid JSON, has a field
the app does not know, a `schema` other than 1, a `minApp` newer than the app, a bad date, an entry that is listed twice, or any entry that
breaks a rule above (an unknown provider, engine, family, sampler, capability or effort; a file without SHA-256 or size, on another host,
at a branch or with `..` in its path; an Ollama tag without a manifest digest). Entries whose id the app has built in are ignored: the
built-in catalog is the floor, the file can only add.

A model the file no longer lists stays in the kept copy while it is installed on that computer, so it keeps working and can be removed
from Settings → Storage.

## Changing it

Edit `catalog/models.json`, raise `date`, and run the tests: `RemoteCatalogTests.TheRepositorysOwnCatalogFileIsValid` reads the file with
the app's own checks. Read the SHA-256 and size of each Hugging Face file at its commit (the API's LFS values) and the Ollama manifest
digest from `ollama list` / `/api/tags` after a test pull.
