## Chatbot Sandbox Frontend

This is a Blazor Server frontend for implementing generative language testbeds.

**Technologies:**
 * .NET 8
 * ASP.NET Core Blazor (Server)
 * Ollama (Llama 3.2)
 * Bootstrap
 * Markdig Markdown to HTML

**Default Passphrase**: `admin`

Shown below is interaction with a rudimentary sales agent named Salesbot, with
auto-generated response suggestions based on chat history.

![Screenshot](https://i.imgur.com/lMBFmub.png)

### Setup
Duplicate `appsettings.json` into `appsettings.{ASPNETCORE_ENVIRONMENT}.json`.

Include the following in your environmental configuration file, replacing the
placeholders with values of your own.

```json
"Ollama": {
    "Endpoint": "http{s?}://{Ollama Host}:{Port}/"
},
"Passphrase": "{Your Pre-shared Passphrase}"
```

The pre-shared passphrase is used to log in from the web browser.