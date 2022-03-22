using FluentAssertions;
using Xunit;

namespace Open.Threading.ReadWrite.Tests;

public class MonitorLockTests : LockTestBase
{
	protected override ILock GetBlockingLock(LockTimeout timeout = default)
		=> new Lock(Sync, timeout);

	[Fact]
	public void BasicTests()
	{
		Assert.Throws<ArgumentNullException>(() => new Lock(null!));

		TestTimeout(() =>
		{
			Assert.Throws<TimeoutException>(() => new Lock(Sync, 1));
			using var sync = new Lock(Sync, 1, false);
			((ILock)sync).LockHeld.Should().BeFalse();
			sync.LockType.Should().Be(LockType.Monitor);
			sync.LockTypeHeld.Should().Be(LockType.None);
		});
		{
			using var sync = new Lock(Sync);
			sync.LockHeld.Should().BeTrue();
			sync.LockTypeHeld.Should().Be(LockType.Monitor);
		}
		{
			using var sync = new Lock(Sync, 1);
			sync.LockHeld.Should().BeTrue();
		}
	}

	struct Test { }

	[Theory]
	[InlineData(null)]
	[InlineData(true)]
	[InlineData(false)]
	[InlineData("hello")]
	[InlineData(0)]
	[InlineData(1)]
	public void InvalidSyncObjectTests(object? syncObject)
	{
		Lock.IsValidSyncObject(syncObject).Should().BeFalse();
		if(syncObject is null)
			Assert.Throws<ArgumentNullException>(() => Lock.AssertSyncObject(syncObject));
		else
			Assert.Throws<ArgumentException>(() => Lock.AssertSyncObject(syncObject));
	}

	[Fact]
	public void StructSyncObjectTests()
		=> InvalidSyncObjectTests(new Test());
}
