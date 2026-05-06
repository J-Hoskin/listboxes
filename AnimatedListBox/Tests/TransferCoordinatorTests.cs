using System;
using Xunit;
using YourApp.Controls;

namespace YourApp.Tests;

public class TransferCoordinatorTests
{
    private readonly TransferCoordinator _sut = new();

    [Fact]
    public void HasAnyPendingReason_ReturnsFalse_WhenNothingStamped()
    {
        Assert.False(_sut.HasAnyPendingReason());
    }

    [Fact]
    public void HasAnyPendingReason_ReturnsTrue_AfterStampEntry()
    {
        _sut.StampEntry(Guid.NewGuid(), EntryReason.FromSibling);
        Assert.True(_sut.HasAnyPendingReason());
    }

    [Fact]
    public void HasAnyPendingReason_ReturnsTrue_AfterStampExit()
    {
        _sut.StampExit(Guid.NewGuid(), ExitReason.ToSibling);
        Assert.True(_sut.HasAnyPendingReason());
    }

    [Fact]
    public void HasAnyPendingReason_ReturnsFalse_AfterConsumed()
    {
        var id = Guid.NewGuid();
        _sut.StampTransfer(id, ExitReason.ToSibling, EntryReason.FromSibling);

        _sut.ConsumeEntryReason(id);
        _sut.ConsumeExitReason(id);

        Assert.False(_sut.HasAnyPendingReason());
    }

    [Fact]
    public void ConsumeEntryReason_ReturnsStampedReason()
    {
        var id = Guid.NewGuid();
        _sut.StampEntry(id, EntryReason.FromSibling);

        Assert.Equal(EntryReason.FromSibling, _sut.ConsumeEntryReason(id));
    }

    [Fact]
    public void ConsumeEntryReason_ReturnsDefault_WhenNothingStamped()
    {
        Assert.Equal(EntryReason.Default, _sut.ConsumeEntryReason(Guid.NewGuid()));
    }

    [Fact]
    public void ConsumeEntryReason_IsOneShot_SecondCallReturnsDefault()
    {
        var id = Guid.NewGuid();
        _sut.StampEntry(id, EntryReason.FromSibling);

        _sut.ConsumeEntryReason(id);
        Assert.Equal(EntryReason.Default, _sut.ConsumeEntryReason(id));
    }

    [Fact]
    public void ConsumeExitReason_ReturnsStampedReason()
    {
        var id = Guid.NewGuid();
        _sut.StampExit(id, ExitReason.ToSibling);

        Assert.Equal(ExitReason.ToSibling, _sut.ConsumeExitReason(id));
    }

    [Fact]
    public void ConsumeExitReason_ReturnsDefault_WhenNothingStamped()
    {
        Assert.Equal(ExitReason.Default, _sut.ConsumeExitReason(Guid.NewGuid()));
    }

    [Fact]
    public void ConsumeExitReason_IsOneShot_SecondCallReturnsDefault()
    {
        var id = Guid.NewGuid();
        _sut.StampExit(id, ExitReason.ToSibling);

        _sut.ConsumeExitReason(id);
        Assert.Equal(ExitReason.Default, _sut.ConsumeExitReason(id));
    }

    [Fact]
    public void StampTransfer_StampsBothEntryAndExit()
    {
        var id = Guid.NewGuid();
        _sut.StampTransfer(id, ExitReason.ToSibling, EntryReason.FromSibling);

        Assert.Equal(EntryReason.FromSibling, _sut.ConsumeEntryReason(id));
        Assert.Equal(ExitReason.ToSibling, _sut.ConsumeExitReason(id));
    }

    [Fact]
    public void StampTransfer_OverwritesPreviousStamp()
    {
        var id = Guid.NewGuid();
        _sut.StampEntry(id, EntryReason.Default);
        _sut.StampTransfer(id, ExitReason.ToSibling, EntryReason.FromSibling);

        Assert.Equal(EntryReason.FromSibling, _sut.ConsumeEntryReason(id));
    }

    [Fact]
    public void MultipleItems_TrackedIndependently()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        _sut.StampEntry(id1, EntryReason.FromSibling);
        _sut.StampEntry(id2, EntryReason.FromNextPage);

        Assert.Equal(EntryReason.FromSibling, _sut.ConsumeEntryReason(id1));
        Assert.Equal(EntryReason.FromNextPage, _sut.ConsumeEntryReason(id2));
    }
}
