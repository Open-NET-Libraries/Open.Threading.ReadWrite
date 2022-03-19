using Xunit;
using FluentAssertions;

namespace Open.Threading.ReadWrite.Tests;

public class ReadTests : ReaderWriterLockSlimTestBase
{
	public ReadTests() : base(new())
	{
	}

	protected override LockType LockType => LockType.Read;

	[Fact]
	public void GetInvalidLockTest()
		=> Assert.Throws<ArgumentOutOfRangeException>(() => Sync.GetLock(LockType.None));

	[Fact]
	public override void EnterTest()
	{
		Assert.Throws<ArgumentNullException>(() =>
			default(ReaderWriterLockSlim)!.EnterReadLock(1));

		Sync.EnterReadLock(default);
		Sync.IsReadLockHeld.Should().BeTrue();
		Sync.ExitReadLock();

		Sync.EnterReadLock(10000);
		Sync.IsReadLockHeld.Should().BeTrue();
		Sync.ExitReadLock();

		using var writeLock = Sync.WriteLock();
		writeLock.LockHeld.Should().BeTrue();
		Sync.IsWriteLockHeld.Should().BeTrue();
		Task.Run(() => Assert.Throws<TimeoutException>(() => Sync.EnterReadLock(1))).Wait();
	}

	[Fact]
	public override bool ActionTest()
	{
		Assert.Throws<ArgumentNullException>(() => Sync.Read(default!));

		var ok = false;
		Sync.Read(() =>
		{
			Sync.IsReadLockHeld.Should().BeTrue();
			ok = true;
		});
		ok.Should().BeTrue();
		return ok;
	}

	[Fact]
	public override bool TryActionTest()
	{
		Assert.Throws<ArgumentNullException>(() => Sync.TryRead(1000, default!));

		var ok = false;
		Sync.TryRead(1000, () =>
		{
			Sync.IsReadLockHeld.Should().BeTrue();
			ok = true;
		}).Should().BeTrue();
		ok.Should().BeTrue();
		return ok;
	}

	protected override void ActionTimeoutCore()
	{
		bool ran = false;
		void Run() => ran = false;
		Assert.Throws<TimeoutException>(() => Sync.Read(1, Run));
		ran.Should().BeFalse();
		Sync.TryRead(1, Run).Should().BeFalse();
		ran.Should().BeFalse();
	}

	[Fact]
	public override bool ValueTest()
	{
		Assert.Throws<ArgumentNullException>(() => Sync.Read(default(Func<bool>)!));

		Sync.Read(() =>
		{
			Sync.IsReadLockHeld.Should().BeTrue();
			return true;
		}).Should().BeTrue();
		return true;
	}

	[Fact]
	public override bool TryValueTest()
	{
		Assert.Throws<ArgumentNullException>(() => Sync.TryRead(1000, out _, default(Func<bool>)!));

		TestTimeout(() => Sync.TryRead(1, out var _, () => true).Should().BeFalse());

		Sync.TryRead(1000, out var ok, () =>
		{
			Sync.IsReadLockHeld.Should().BeTrue();
			return true;
		}).Should().BeTrue();
		ok.Should().BeTrue();
		return ok;
	}
}
