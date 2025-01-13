## Chatbot Sandbox Frontend

This is a Blazor Server frontend for implementing generative language testbeds.

**Technologies:**
 * .NET 8
 * ASP.NET Core Blazor (Server)
 * Ollama (Llama 3.2)
 * WebUI API
 * Bootstrap
 * Markdig Markdown to HTML

**Default Passphrase**: `admin`

Generalbot is able to generate images via API access in WebUI.

<img src="https://i.imgur.com/ugx9SFw.png" width="400" />

It will read provided URLs and discuss or summarize content.

It tries very hard to flatter you!

<img src="https://i.imgur.com/P2OgTsb.png" width="400" />

Shown below is interaction with a rudimentary sales agent named Salesbot, with
auto-generated response suggestions based on chat history.

<img src="https://i.imgur.com/lMBFmub.png" width="500" />

### Setup
Duplicate `appsettings.json` into `appsettings.{ASPNETCORE_ENVIRONMENT}.json`.

Include the following in your environmental configuration file, replacing the
placeholders with values of your own.

```json
"Ollama": {
    "Endpoint": "http{s?}://{Ollama Host}:{Port}/"
},
"WebUi": {
    "Endpoint": "http{s?}://{WebUI Host}:{Port}/"
},
"Passphrase": "{Your Pre-shared Passphrase}"
```

The pre-shared passphrase is used to log in from the web browser.