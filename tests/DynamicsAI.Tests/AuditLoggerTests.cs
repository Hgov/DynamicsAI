using DynamicsAI.Infrastructure.Logging;
using Microsoft.Extensions.Logging;
using Moq;

namespace DynamicsAI.Tests;

public class AuditLoggerTests
{
    [Fact]
    public void LogToolCall_Success_LogsAtInformationLevel()
    {
        var logger = new Mock<ILogger<AuditLogger>>();
        var audit = new AuditLogger(logger.Object);

        audit.LogToolCall("tenant-abc", "dynamics_get_metadata", 320, true);

        logger.Verify(l => l.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            null,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public void LogToolCall_Failure_LogsAtInformationLevel()
    {
        var logger = new Mock<ILogger<AuditLogger>>();
        var audit = new AuditLogger(logger.Object);

        audit.LogToolCall("tenant-abc", "dynamics_execute_query", 150, false);

        logger.Verify(l => l.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            null,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public void LogToolCall_DoesNotThrow_ForAnyInput()
    {
        var audit = new AuditLogger(Mock.Of<ILogger<AuditLogger>>());

        var ex = Record.Exception(() =>
            audit.LogToolCall("", "tool", 0, false));

        Assert.Null(ex);
    }
}
