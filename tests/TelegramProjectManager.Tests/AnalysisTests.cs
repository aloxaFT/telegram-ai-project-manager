using System.Text.Json;
using System.Text.Json.Nodes;
using TelegramProjectManager.AI;
using TelegramProjectManager.Domain;

namespace TelegramProjectManager.Tests;

public sealed class AnalysisTests
{
    private static readonly AnalysisContext Context = new(DateTimeOffset.Parse("2026-11-05T12:00:00Z"), "Europe/Warsaw", "TradeFlow AI",
        [new ChatMessage { TelegramMessageId = 50, TelegramUserId = 7, Text = "I will interview ACME tomorrow." }],
        [new ProjectMember { TelegramUserId = 7, IsActive = true }]);
    private const string Valid = """
        {"tasks":[{"title":"Interview ACME","description":"Customer discovery","ownerTelegramUserId":7,
        "dueAt":"2026-11-06T15:00:00+01:00","priority":"High","sourceMessageId":50,"confidence":0.9,
        "missingInformation":[]}],"decisions":[],"blockers":[]}
        """;

    [Fact]
    public void Valid_structured_task_preserves_evidence_and_does_not_become_a_commitment()
    {
        var result = AnalysisValidator.Parse(Valid, Context);
        var task = Assert.Single(result.Tasks);
        Assert.Equal(7, task.OwnerTelegramUserId);
        Assert.Equal(50, task.SourceMessageId);
        Assert.Equal("2026-11-06T15:00:00+01:00", task.DueAt);
    }

    [Theory]
    [InlineData("sourceMessageId", "999")]
    [InlineData("ownerTelegramUserId", "999")]
    [InlineData("ownerTelegramUserId", "\"@someone\"")]
    [InlineData("dueAt", "\"tomorrow\"")]
    [InlineData("dueAt", "\"2026-11-06T15:00:00\"")]
    [InlineData("dueAt", "\"2026-02-30T15:00:00Z\"")]
    [InlineData("dueAt", "\"2025-01-01T15:00:00Z\"")]
    [InlineData("dueAt", "\"2030-01-01T15:00:00Z\"")]
    [InlineData("priority", "\"ExecuteCommand\"")]
    [InlineData("priority", "\"2\"")]
    [InlineData("confidence", "1.1")]
    [InlineData("confidence", "-0.1")]
    [InlineData("title", "\" \"")]
    [InlineData("missingInformation", "null")]
    [InlineData("command", "\"/done 1\"")]
    public void Untrusted_fields_are_rejected(string field, string json)
    {
        var root = JsonNode.Parse(Valid)!;
        root["tasks"]![0]![field] = JsonNode.Parse(json);
        Assert.Throws<AnalysisValidationException>(() => AnalysisValidator.Parse(root.ToJsonString(), Context));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"tasks\":null,\"decisions\":[],\"blockers\":[]}")]
    [InlineData("```json\n{}\n```")]
    [InlineData("{\"tasks\":[],\"decisions\":[{\"text\":\"Agree\",\"sourceMessageId\":999}],\"blockers\":[]}")]
    public void Malformed_or_unsupported_contracts_fail_closed(string json) =>
        Assert.Throws<AnalysisValidationException>(() => AnalysisValidator.Parse(json, Context));

    [Fact]
    public void Withdrawn_member_cannot_be_proposed_as_owner()
    {
        var context = Context with { Members = [new ProjectMember { TelegramUserId = 7, IsActive = false }] };
        Assert.Throws<AnalysisValidationException>(() => AnalysisValidator.Parse(Valid, context));
    }

    [Fact]
    public void Oversized_or_excessive_output_is_rejected()
    {
        Assert.Throws<AnalysisValidationException>(() => AnalysisValidator.Parse(new string('x', 40001), Context));
        var root = JsonNode.Parse(Valid)!;
        for (var i = 0; i < 5; i++) root["tasks"]!.AsArray().Add(root["tasks"]![0]!.DeepClone());
        Assert.Throws<AnalysisValidationException>(() => AnalysisValidator.Parse(root.ToJsonString(), Context));
    }
}
