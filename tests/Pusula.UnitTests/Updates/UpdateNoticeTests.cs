using Pusula.Updates;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Updates;

public sealed class UpdateNoticeTests
{
    private static ReleaseTag Tag(string text)
    {
        ReleaseTag.TryParse(text, out ReleaseTag? tag).ShouldBeTrue(text);
        return tag!;
    }

    [Fact]
    public void Snapshot_CheckOff_IsOffWithTheCurrentVersionOnly()
    {
        var notice = new UpdateNotice(enabled: false, "0.3.0", new ManualTimeProvider());

        notice.Enabled.ShouldBeFalse();
        notice.Snapshot().ShouldBe(new UpdateResponse(UpdateState.Off, "0.3.0"));
    }

    [Fact]
    public void Snapshot_CheckOnAndNothingFoundYet_IsNotChecked()
    {
        var notice = new UpdateNotice(enabled: true, "0.3.0", new ManualTimeProvider());

        notice.Enabled.ShouldBeTrue();
        notice.Snapshot().ShouldBe(new UpdateResponse(UpdateState.NotChecked, "0.3.0"));
    }

    [Fact]
    public void RecordLatest_ANewerRelease_IsNewerWithItsVersionPageAndTime()
    {
        var clock = new ManualTimeProvider();
        var notice = new UpdateNotice(enabled: true, "0.3.0", clock);

        notice.RecordLatest(Tag("v0.4.0"));

        notice.Snapshot().ShouldBe(new UpdateResponse(UpdateState.Newer, "0.3.0", "0.4.0", "https://github.com/faraday208/pusula/releases/tag/v0.4.0", clock.GetUtcNow()));
    }

    [Theory]
    [InlineData("0.3.0", "v0.3.0")]
    [InlineData("0.3.0", "v0.2.9")]
    [InlineData("0.4.0", "v0.4.0-rc.1")]
    [InlineData("0.4.0-rc.2", "v0.4.0-rc.1")]
    [InlineData("1.0.0", "v0.99.0")]
    public void RecordLatest_TheSameOrAnOlderRelease_IsUpToDate(string current, string latest)
    {
        var notice = new UpdateNotice(enabled: true, current, new ManualTimeProvider());

        notice.RecordLatest(Tag(latest));

        UpdateResponse answer = notice.Snapshot();
        answer.State.ShouldBe(UpdateState.UpToDate);
        answer.Latest.ShouldBe(latest[1..]);
        answer.Url.ShouldBe("https://github.com/faraday208/pusula/releases/tag/" + latest);
    }

    [Theory]
    [InlineData("0.4.0-rc.1", "v0.4.0")]
    [InlineData("0.3.0", "v0.3.1")]
    [InlineData("0.3.0", "v1.0.0-alpha")]
    [InlineData("0.9.0", "v0.10.0")]
    public void RecordLatest_AHigherVersion_IsNewer(string current, string latest)
    {
        var notice = new UpdateNotice(enabled: true, current, new ManualTimeProvider());

        notice.RecordLatest(Tag(latest));

        notice.Snapshot().State.ShouldBe(UpdateState.Newer);
    }

    [Theory]
    [InlineData("dev")]
    [InlineData("")]
    [InlineData("0.3")]
    public void RecordLatest_ACurrentVersionThatCannotBeRead_IsNeverNewer(string current)
    {
        var notice = new UpdateNotice(enabled: true, current, new ManualTimeProvider());

        notice.RecordLatest(Tag("v99.0.0"));

        UpdateResponse answer = notice.Snapshot();
        answer.State.ShouldBe(UpdateState.UpToDate);
        answer.Latest.ShouldBe("99.0.0");
    }

    [Fact]
    public void RecordFailure_WithNoEarlierAnswer_IsFailedWithTheTimeAndNoRelease()
    {
        var clock = new ManualTimeProvider();
        var notice = new UpdateNotice(enabled: true, "0.3.0", clock);

        notice.RecordFailure();

        notice.Snapshot().ShouldBe(new UpdateResponse(UpdateState.Failed, "0.3.0", CheckedAt: clock.GetUtcNow()));
    }

    [Fact]
    public void RecordFailure_AfterAnAnswer_LeavesTheAnswerAsItWas()
    {
        var clock = new ManualTimeProvider();
        var notice = new UpdateNotice(enabled: true, "0.3.0", clock);
        notice.RecordLatest(Tag("v0.4.0"));
        UpdateResponse found = notice.Snapshot();

        clock.Advance(TimeSpan.FromDays(1));
        notice.RecordFailure();

        notice.Snapshot().ShouldBe(found);
    }

    [Fact]
    public void RecordLatest_AfterAFailure_ReplacesIt()
    {
        var clock = new ManualTimeProvider();
        var notice = new UpdateNotice(enabled: true, "0.3.0", clock);
        notice.RecordFailure();

        clock.Advance(TimeSpan.FromDays(1));
        notice.RecordLatest(Tag("v0.3.0"));

        UpdateResponse answer = notice.Snapshot();
        answer.State.ShouldBe(UpdateState.UpToDate);
        answer.CheckedAt.ShouldBe(clock.GetUtcNow());
    }

    [Fact]
    public void RecordLatest_ALaterRelease_ReplacesAnEarlierAnswer()
    {
        var notice = new UpdateNotice(enabled: true, "0.3.0", new ManualTimeProvider());
        notice.RecordLatest(Tag("v0.4.0"));

        notice.RecordLatest(Tag("v0.5.0"));

        notice.Snapshot().Latest.ShouldBe("0.5.0");
    }
}
