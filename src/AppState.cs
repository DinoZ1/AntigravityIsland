using System;

namespace AntigravityIsland.Model
{
    public enum AgentStatus
    {
        Idle,
        Thinking,
        WaitingPermission,
        ExecutingTool,
        Finished,
        Error
    }

    public class ToolPermissionRequest
    {
        public string RequestId { get; set; }
        public string ConversationId { get; set; }
        public string ToolName { get; set; }
        public string ToolArgs { get; set; }
        public int StepIdx { get; set; }
        public int Pid { get; set; }
        public DateTime Timestamp { get; set; }

        public ToolPermissionRequest()
        {
            Timestamp = DateTime.UtcNow;
            RequestId = Guid.NewGuid().ToString("N");
        }
    }
}
