using FluentAssertions;
using Xunit;

namespace Open.Threading.ReadWrite.Tests;

public abstract class ReaderWriterLockSlimTestBase : LockTestBase
{
	protected ReaderWriterLockSlimTestBase(ReaderWriterLockSlim sync) : base(sync)
	{
	}

	protected abstract LockType LockType { get; }

	protected ILock GetNullLock() => (default(ReaderWriterLockSlim)!).GetLock(LockType);

	protected ILock GetLock(LockTimeout timeout = default, bool throwIfTimeout = true)
		 => Sync.GetLock(LockType, timeout, throwIfTimeout);

	[Fact]
	public void LockContractTest()
	{
		Assert.Throws<ArgumentNullException>(GetNullLock);
		Assert.Throws<ArgumentOutOfRangeException>(() => GetLock(-2));
		using var sync = GetLock();
		sync.LockType.Should().Be(LockType);
		sync.LockTypeHeld.Should().Be(LockType);
	}

	public abstract void EnterTest();

	public abstract bool ActionTest();

	public abstract bool TryActionTest();

	public abstract bool ValueTest();

	public abstract bool TryValueTest();

	protected abstract void ActionTimeoutCore();

	[Fact]
	public void ActionTimeoutTest() => TestTimeout(() =>
	{
		using var sync = GetLock(1, false);
		sync.LockHeld.Should().BeFalse();
		sync.LockType.Should().Be(LockType);
		sync.LockTypeHeld.Should().Be(LockType.None);
		ActionTimeoutCore();
	});
}
