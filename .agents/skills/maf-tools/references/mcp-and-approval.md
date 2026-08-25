# MCP and approval

MAF .NET can use tools discovered through the official MCP C# SDK and expose them as AI tools/functions to supported agents.

Use MCP when you want an interoperable external tool server boundary, not simply because an API exists.

Checklist:

- trust the server/operator;
- constrain transports/endpoints;
- keep credentials out of prompts/tool descriptions;
- expose only needed tools;
- validate tenant/user scope outside the model;
- log/audit consequential calls appropriately;
- handle cancellation/timeouts;
- use approval for destructive or expensive actions;
- assume remote tool output can contain malicious/prompt-injection content.

Hosted/provider-owned MCP is provider-specific. Keep it behind a capability adapter if portability matters. Local/client-side MCP integration is usually more portable across compatible agent/provider combinations.
