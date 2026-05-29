using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace Memori.Mcp;

[McpServerPromptType]
public static class MemoriPrompts
{
    [McpServerPrompt(Name = "remember-context", Title = "Remember Context")]
    [Description("Walk through storing the current session context as durable memories")]
    public static GetPromptResult RememberContext(
        [Description("Brief description of the current task or session")] string context)
    {
        return new GetPromptResult
        {
            Messages =
            [
                new PromptMessage
                {
                    Role = Role.User,
                    Content = new TextContentBlock
                    {
                        Text =
                            $"Store the following context as durable memories for future recall:\n\n" +
                            $"{context}\n\n" +
                            "Use remember to store key facts about the user's preferences, project setup, " +
                            "decisions made, and anything worth recalling in future sessions."
                    }
                }
            ],
            Description = $"Remember session context: {context}"
        };
    }

    [McpServerPrompt(Name = "recall-session", Title = "Recall Session")]
    [Description("Prompt to recall and restore context from a past session")]
    public static GetPromptResult RecallSession(
        [Description("What you want to recall (e.g., 'project setup decisions')")] string topic)
    {
        return new GetPromptResult
        {
            Messages =
            [
                new PromptMessage
                {
                    Role = Role.User,
                    Content = new TextContentBlock
                    {
                        Text =
                            $"Recall information about '{topic}' from stored memories.\n\n" +
                            "Use search to find relevant memories.\n" +
                            "Use get to retrieve full details of specific memories."
                    }
                }
            ],
            Description = $"Recall: {topic}"
        };
    }

    [McpServerPrompt(Name = "review-memories", Title = "Review Memories")]
    [Description("Review and optionally prune stored memories")]
    public static GetPromptResult ReviewMemories()
    {
        return new GetPromptResult
        {
            Messages =
            [
                new PromptMessage
                {
                    Role = Role.User,
                    Content = new TextContentBlock
                    {
                        Text =
                            "Review the stored memories and clean up if needed.\n\n" +
                            "Use list to see all memories.\n" +
                            "Use get to inspect individual memories.\n" +
                            "Use delete to remove outdated or incorrect memories.\n" +
                            "Use update to correct memory content.\n" +
                            "Use clear to wipe all memories for a fresh start."
                    }
                }
            ],
            Description = "Review and prune memories"
        };
    }
}
