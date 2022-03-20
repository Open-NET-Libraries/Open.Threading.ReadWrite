using Xunit;
using FluentAssertions;

namespace Open.Threading.ReadWrite.Tests;

public class ReadUpgradableTests : ReaderWriterLockSlimTestBase
{
	protected ReadUpgradableTests(ReaderWriterLockSlim sync) : base(sync)
	{
		WriteTests = new WriteTests(sync);
	}

	public ReadUpgradableTests() : this(new()) { }

	readonly WriteTests WriteTests;

	protected override LockType LockType => LockType.UpgradableRead;

	protected override ILock GetBlockingLock(LockTimeout timeout = default)
		=> new UpgradableReadLock(Sync, timeout);

	[Fact]
	public override void EnterTest()
	{
		Assert.Throws<ArgumentNullException>(() =>
			default(ReaderWriterLockSlim)!.EnterUpgradeableReadLock(1));

		Sync.EnterUpgradeableReadLock(default);
		Sync.IsUpgradeableReadLockHeld.Should().BeTrue();
		Sync.ExitUpgradeableReadLock();

		Sync.EnterUpgradeableReadLock(10000);
		Sync.IsUpgradeableReadLockHeld.Should().BeTrue();
		Sync.ExitUpgradeableReadLock();

		using var upgradable = Sync.UpgradableReadLock();
		upgradable.LockHeld.Should().BeTrue();
		Task.Run(() => Assert.Throws<TimeoutException>(() => Sync.EnterUpgradeableReadLock(1))).Wait();
	}

	[Fact]
	public override bool ActionTest()
	{
		Assert.Throws<ArgumentNullException>(() => Sync.ReadUpgradeable(default!));

		var ok = false;
		Sync.ReadUpgradeable(() =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().BeTrue();
			ok = WriteTests.ActionTest();
		});
		ok.Should().BeTrue();
		return ok;
	}

	[Fact]
	public override bool TryActionTest()
	{
		Assert.Throws<ArgumentNullException>(() => Sync.TryReadUpgradable(1000, default!));

		var ok = false;
		var held = Sync.TryReadUpgradable(1000, () =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().BeTrue();
			ok = true;
		});
		held.Should().BeTrue();
		ok.Should().BeTrue();
		return held && ok;
	}

	protected override void ActionTimeoutCore()
	{
		Sync.TryGetLock(LockType.UpgradableRead, 1).Should().BeNull();

		bool ran = false;
		void Run() => ran = false;
		Assert.Throws<TimeoutException>(() => Sync.ReadUpgradeable(1, Run));
		ran.Should().BeFalse();
		Sync.TryReadUpgradable(1, Run).Should().BeFalse();
		ran.Should().BeFalse();
	}

	[Fact]
	public override bool ValueTest()
	{
		Assert.Throws<ArgumentNullException>(() => Sync.ReadUpgradeable(default(Func<bool>)!));

		var ok = Sync.ReadUpgradeable(() =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().BeTrue();
			return WriteTests.ValueTest();
		});
		ok.Should().BeTrue();
		return ok;
	}

	[Fact]
	public override bool TryValueTest()
	{
		Assert.Throws<ArgumentNullException>(() => Sync.TryReadUpgradable(1000, out _, default(Func<bool>)!));

		TestTimeout(() => Sync.TryReadUpgradable(1, out var _, () => true).Should().BeFalse());

		var held = Sync.TryReadUpgradable(1000, out var ok, () =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().BeTrue();
			return true;
		});
		held.Should().BeTrue();
		ok.Should().BeTrue();
		return ok && held;
	}
}
