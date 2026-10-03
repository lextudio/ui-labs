# LeXtudio.DevFlow.Mcp

MCP server that lets AI agents inspect and drive LeXtudio DevFlow desktop agents.

```csharp
await DevFlowMcpServer.RunAsync(defaultAgentPort: 9223);
```

The server speaks MCP over stdio, so a host process must not write to stdout.

## Tools

| Tool | Purpose |
| --- | --- |
| `devflow_tree` | Visual tree with element IDs, types, bounds and text |
| `devflow_element` | One element by ID |
| `devflow_tap` | Click an element |
| `devflow_fill` | Set the text of an editable control |
| `devflow_clear` | Empty an editable control |
| `devflow_screenshot` | PNG of the window or one element |
| `devflow_assert` | PASS/FAIL for a property against an expected value |

Every tool maps to an endpoint the desktop agents actually serve. Tools for capabilities the agents
answer with `not_supported` are left out until those are implemented - storage, sensors, jobs and
platform files at the time of writing.

`devflow_assert` resolves properties from the tree snapshot, so the readable names are what
`devflow_element` reports: the protocol ones (`Text`, `IsVisible`, `IsEnabled`, `IsFocused`,
`Opacity`, `AutomationId`, `Type`, `FullType`, `Value`) plus whatever the framework adds.

The agent port is resolved from the broker when not given explicitly, so an AI agent does not need to be
told where the app listens.

## Logging

`RunAsync` discards log output by default, which leaves a failing tool reported only as "an error
occurred". Pass a stderr `ILoggerFactory` when hosting the process to see the real exception.
