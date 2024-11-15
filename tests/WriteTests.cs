﻿using Xunit;
using FluentAssertions;

namespace Open.Threading.ReadWrite.Tests;

public class WriteTests(ReaderWriterLockSlim? sync = null)
	: ReaderWriterLockSlimTestBase(sync ?? new())
{
	protected override LockType LockType => LockType.Write;

	[Fact]
	public override void EnterTest()
	{
		Assert.Throws<ArgumentNullException>(() =>
			default(ReaderWriterLockSlim)!.EnterWriteLock(1));

		Sync.EnterWriteLock(default);
		Sync.IsWriteLockHeld.Should().BeTrue();
		Sync.ExitWriteLock();

		Sync.EnterWriteLock(10000);
		Sync.IsWriteLockHeld.Should().BeTrue();
		Sync.ExitWriteLock();

		using var writeLock = Sync.WriteLock();
		writeLock.LockHeld.Should().BeTrue();
		Sync.IsWriteLockHeld.Should().BeTrue();

		// Avoid's recursion.
		Task.Factory.StartNew(
			() => Assert.Throws<TimeoutException>(() => Sync.EnterWriteLock(1)),
			TaskCreationOptions.LongRunning)
			.Wait();
	}

	[Fact]
	public override void ActionTest()
	{
		Assert.Throws<ArgumentNullException>(() => Sync.Write(default!));

		var ok = false;
		Sync.Write(() =>
		{
			Sync.IsWriteLockHeld.Should().BeTrue();
			ok = true;
		});
		ok.Should().BeTrue();
	}

	[Fact]
	public override void TryActionTest()
	{
		Assert.Throws<ArgumentNullException>(() => Sync.TryWrite(1000, default!));

		var ok = false;
		Sync.TryWrite(1000, () =>
		{
			Sync.IsWriteLockHeld.Should().BeTrue();
			ok = true;
		}).Should().BeTrue();
		ok.Should().BeTrue();
	}

	protected override void ActionTimeoutCore()
	{
		Sync.TryGetLock(LockType.Write, 1).Should().BeNull();

		bool ran = false;
		void Run() => ran = false;
		Assert.Throws<TimeoutException>(() => Sync.Write(1, Run));
		ran.Should().BeFalse();
		Sync.TryWrite(1, Run).Should().BeFalse();
		ran.Should().BeFalse();
	}

	[Fact]
	public override void ValueTest()
	{
		Assert.Throws<ArgumentNullException>(() => Sync.Write(default(Func<bool>)!));

		var ok = Sync.Write(() =>
		{
			Sync.IsWriteLockHeld.Should().BeTrue();
			return true;
		});
		ok.Should().BeTrue();
	}

	[Fact]
	public override void TryValueTest()
	{
		Assert.Throws<ArgumentNullException>(() => Sync.TryWrite(1000, out _, default(Func<bool>)!));

		TestTimeout(() => Sync.TryWrite(1, out var _, () => true).Should().BeFalse());

		Sync.TryWrite(1000, out var ok, () =>
		{
			Sync.IsWriteLockHeld.Should().BeTrue();
			return true;
		}).Should().BeTrue();
		ok.Should().BeTrue();
	}

	[Fact]
	public void ConditionalTest()
	{
		bool ran = false;
		Assert.Throws<ArgumentNullException>(() => Sync.WriteConditional(1000, () => false, default!));
		Assert.Throws<ArgumentNullException>(() => Sync.WriteConditional(1000, _ => false, default!));
		Assert.Throws<ArgumentNullException>(() => Sync.WriteConditional(1000, default(Func<bool>)!, () => { }));
		Assert.Throws<ArgumentNullException>(() => Sync.WriteConditional(1000, default(Func<bool, bool>)!, () => { }));
		Assert.Throws<ArgumentNullException>(() => Sync.Handler().WriteConditional(1000, () => false, default!));
		Assert.Throws<ArgumentNullException>(() => Sync.Handler().WriteConditional(1000, _ => false, default!));
		Assert.Throws<ArgumentNullException>(() => Sync.Handler().WriteConditional(1000, default(Func<bool>)!, () => { }));
		Assert.Throws<ArgumentNullException>(() => Sync.Handler().WriteConditional(1000, default(Func<bool, bool>)!, () => { }));

		ran.Should().BeFalse();

		void Run()
		{
			Sync.IsWriteLockHeld.Should().BeTrue();
			ran = true;
		}

		Sync.WriteConditional(() =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().BeTrue();
			return false;
		}, Run).Should().BeFalse();
		ran.Should().BeFalse();

		Sync.Handler().WriteConditional(() =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().BeTrue();
			return false;
		}, Run).Should().BeFalse();
		ran.Should().BeFalse();

		TestTimeout(() =>
		{
			Assert.Throws<TimeoutException>(() => Sync.WriteConditional(1, () => true, Run));
			Assert.Throws<TimeoutException>(() => Sync.WriteConditional(1, _ => true, Run));
			Assert.Throws<TimeoutException>(() => Sync.Handler().WriteConditional(1, () => true, Run));
			Assert.Throws<TimeoutException>(() => Sync.Handler().WriteConditional(1, _ => true, Run));
			ran.Should().BeFalse();
		});

		Sync.WriteConditional(() =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().BeTrue();
			return true;
		}, Run).Should().BeTrue();
		ran.Should().BeTrue();

		ran = false;
		Sync.Handler().WriteConditional(() =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().BeTrue();
			return true;
		}, Run).Should().BeTrue();
		ran.Should().BeTrue();

		ran = false;
		Sync.WriteConditional(hasLock =>
		{
			hasLock.Should().BeFalse();
			Sync.IsUpgradeableReadLockHeld.Should().BeFalse();
			return false;
		}, Run).Should().BeFalse();
		ran.Should().BeFalse();

		Sync.WriteConditional(hasLock =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().Be(hasLock);
			return !hasLock;
		}, Run).Should().BeFalse();
		ran.Should().BeFalse();

		Sync.WriteConditional(hasLock =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().Be(hasLock);
			return true;
		}, Run).Should().BeTrue();
		ran.Should().BeTrue();

		ran = false;
		Sync.Handler().WriteConditional(hasLock =>
		{
			hasLock.Should().BeFalse();
			Sync.IsUpgradeableReadLockHeld.Should().BeFalse();
			return false;
		}, Run).Should().BeFalse();
		ran.Should().BeFalse();

		Sync.Handler().WriteConditional(hasLock =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().Be(hasLock);
			return !hasLock;
		}, Run).Should().BeFalse();
		ran.Should().BeFalse();

		Sync.Handler().WriteConditional(hasLock =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().Be(hasLock);
			return true;
		}, Run).Should().BeTrue();
		ran.Should().BeTrue();
	}

	[Fact]
	public void ConditionalValueTest()
	{
		bool ran = false;
		Assert.Throws<ArgumentNullException>(() => Sync.WriteConditional(1000, ref ran, () => false, default!));
		Assert.Throws<ArgumentNullException>(() => Sync.WriteConditional(1000, ref ran, _ => false, default!));
		Assert.Throws<ArgumentNullException>(() => Sync.WriteConditional(1000, ref ran, default(Func<bool>)!, () => false));
		Assert.Throws<ArgumentNullException>(() => Sync.WriteConditional(1000, ref ran, default(Func<bool, bool>)!, () => false));
		Assert.Throws<ArgumentNullException>(() => Sync.Handler().WriteConditional(1000, ref ran, () => false, default!));
		Assert.Throws<ArgumentNullException>(() => Sync.Handler().WriteConditional(1000, ref ran, _ => false, default!));
		Assert.Throws<ArgumentNullException>(() => Sync.Handler().WriteConditional(1000, ref ran, default(Func<bool>)!, () => false));
		Assert.Throws<ArgumentNullException>(() => Sync.Handler().WriteConditional(1000, ref ran, default(Func<bool, bool>)!, () => false));

		ran.Should().BeFalse();

		bool Run()
		{
			Sync.IsWriteLockHeld.Should().BeTrue();
			ran = true;
			return true;
		}

		bool result = false;
		Sync.WriteConditional(ref result, () =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().BeTrue();
			return false;
		}, Run).Should().BeFalse();
		ran.Should().BeFalse();
		result.Should().BeFalse();

		Sync.Handler().WriteConditional(ref result, () =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().BeTrue();
			return false;
		}, Run).Should().BeFalse();
		ran.Should().BeFalse();
		result.Should().BeFalse();

		TestTimeout(() =>
		{
			Assert.Throws<TimeoutException>(() => Sync.WriteConditional(1, ref result, () => true, Run));
			Assert.Throws<TimeoutException>(() => Sync.WriteConditional(1, ref result, _ => true, Run));
			ran.Should().BeFalse();
			result.Should().BeFalse();
		});

		Sync.WriteConditional(ref result, () =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().BeTrue();
			return true;
		}, Run).Should().BeTrue();
		ran.Should().BeTrue();
		result.Should().BeTrue();

		result = ran = false;
		Sync.Handler().WriteConditional(ref result, () =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().BeTrue();
			return true;
		}, Run).Should().BeTrue();
		ran.Should().BeTrue();
		result.Should().BeTrue();

		result = ran = false;
		Sync.WriteConditional(ref result, hasLock =>
		{
			hasLock.Should().BeFalse();
			Sync.IsUpgradeableReadLockHeld.Should().BeFalse();
			return false;
		}, Run).Should().BeFalse();
		ran.Should().BeFalse();
		result.Should().BeFalse();

		Sync.WriteConditional(ref result, hasLock =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().Be(hasLock);
			return !hasLock;
		}, Run).Should().BeFalse();
		ran.Should().BeFalse();
		result.Should().BeFalse();

		Sync.WriteConditional(ref result, hasLock =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().Be(hasLock);
			return true;
		}, Run).Should().BeTrue();
		ran.Should().BeTrue();
		result.Should().BeTrue();

		result = ran = false;
		Sync.Handler().WriteConditional(ref result, hasLock =>
		{
			hasLock.Should().BeFalse();
			Sync.IsUpgradeableReadLockHeld.Should().BeFalse();
			return false;
		}, Run).Should().BeFalse();
		ran.Should().BeFalse();
		result.Should().BeFalse();

		Sync.Handler().WriteConditional(ref result, hasLock =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().Be(hasLock);
			return !hasLock;
		}, Run).Should().BeFalse();
		ran.Should().BeFalse();
		result.Should().BeFalse();

		Sync.Handler().WriteConditional(ref result, hasLock =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().Be(hasLock);
			return true;
		}, Run).Should().BeTrue();
		ran.Should().BeTrue();
		result.Should().BeTrue();
	}

	[Fact]
	public void TryConditionalTest()
	{
		bool ran = false;
		Assert.Throws<ArgumentNullException>(
			() => Sync.TryWriteConditional(1000, () => false, default!));
		Assert.Throws<ArgumentNullException>(
			() => Sync.TryWriteConditional(1000, _ => false, default!));
		Assert.Throws<ArgumentNullException>(
			() => Sync.TryWriteConditional(1000, default(Func<bool>)!, () => { }));
		Assert.Throws<ArgumentNullException>(
			() => Sync.TryWriteConditional(1000, default(Func<bool, bool>)!, () => { }));

		ran.Should().BeFalse();

		void Run()
		{
			Sync.IsWriteLockHeld.Should().BeTrue();
			ran = true;
		}

		Sync.TryWriteConditional(1000, () =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().BeTrue();
			return false;
		}, Run).Should().BeFalse();
		ran.Should().BeFalse();

		Sync.Handler().TryWriteConditional(1000, () =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().BeTrue();
			return false;
		}, Run).Should().BeFalse();
		ran.Should().BeFalse();

		TestTimeout(() =>
		{
			Sync.TryWriteConditional(1, () =>
			{
				Sync.IsUpgradeableReadLockHeld.Should().BeTrue();
				return true;
			}, Run).Should().BeFalse();
			Sync.TryWriteConditional(1, hasLock =>
			{
				Sync.IsUpgradeableReadLockHeld.Should().Be(hasLock);
				return true;
			}, Run).Should().BeFalse();
			ran.Should().BeFalse();

			Sync.Handler().TryWriteConditional(1, () =>
			{
				Sync.IsUpgradeableReadLockHeld.Should().BeTrue();
				return true;
			}, Run).Should().BeFalse();
			Sync.Handler().TryWriteConditional(1, hasLock =>
			{
				Sync.IsUpgradeableReadLockHeld.Should().Be(hasLock);
				return true;
			}, Run).Should().BeFalse();
			ran.Should().BeFalse();
		});

		Sync.TryWriteConditional(1000, () =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().BeTrue();
			return true;
		}, Run).Should().BeTrue();
		ran.Should().BeTrue();

		ran = false;
		Sync.Handler().TryWriteConditional(1000, () =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().BeTrue();
			return true;
		}, Run).Should().BeTrue();
		ran.Should().BeTrue();

		ran = false;
		Sync.TryWriteConditional(1000, hasLock =>
		{
			hasLock.Should().BeFalse();
			Sync.IsUpgradeableReadLockHeld.Should().BeFalse();
			return false;
		}, Run).Should().BeFalse();
		ran.Should().BeFalse();

		Sync.TryWriteConditional(1000, hasLock =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().Be(hasLock);
			return !hasLock;
		}, Run).Should().BeFalse();
		ran.Should().BeFalse();

		Sync.TryWriteConditional(1000, hasLock =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().Be(hasLock);
			return true;
		}, Run).Should().BeTrue();
		ran.Should().BeTrue();

		ran = false;
		Sync.Handler().TryWriteConditional(1000, hasLock =>
		{
			hasLock.Should().BeFalse();
			Sync.IsUpgradeableReadLockHeld.Should().BeFalse();
			return false;
		}, Run).Should().BeFalse();
		ran.Should().BeFalse();

		Sync.Handler().TryWriteConditional(1000, hasLock =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().Be(hasLock);
			return !hasLock;
		}, Run).Should().BeFalse();
		ran.Should().BeFalse();

		Sync.Handler().TryWriteConditional(1000, hasLock =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().Be(hasLock);
			return true;
		}, Run).Should().BeTrue();
		ran.Should().BeTrue();
	}

	[Fact]
	public void TryConditionalValueTest()
	{
		bool ran = false;
		Assert.Throws<ArgumentNullException>(() => Sync.TryWriteConditional(1000, ref ran, () => false, default!));
		Assert.Throws<ArgumentNullException>(() => Sync.TryWriteConditional(1000, ref ran, _ => false, default!));
		Assert.Throws<ArgumentNullException>(() => Sync.TryWriteConditional(1000, ref ran, default(Func<bool>)!, () => false));
		Assert.Throws<ArgumentNullException>(() => Sync.TryWriteConditional(1000, ref ran, default(Func<bool, bool>)!, () => false));

		ran.Should().BeFalse();

		bool Run()
		{
			Sync.IsWriteLockHeld.Should().BeTrue();
			ran = true;
			return true;
		}

		bool result = false;
		Sync.TryWriteConditional(1000, ref result, () =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().BeTrue();
			return false;
		}, Run).Should().BeFalse();
		ran.Should().BeFalse();
		result.Should().BeFalse();

		Sync.Handler().TryWriteConditional(1000, ref result, () =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().BeTrue();
			return false;
		}, Run).Should().BeFalse();
		ran.Should().BeFalse();
		result.Should().BeFalse();

		TestTimeout(() =>
		{
			Sync.TryWriteConditional(1, ref result, () =>
			{
				Sync.IsUpgradeableReadLockHeld.Should().BeFalse();
				return true;
			}, Run).Should().BeFalse();
			Sync.TryWriteConditional(1, ref result, hasLock =>
			{
				Sync.IsUpgradeableReadLockHeld.Should().Be(hasLock);
				return true;
			}, Run).Should().BeFalse();
			ran.Should().BeFalse();
			result.Should().BeFalse();

			Sync.Handler().TryWriteConditional(1, ref result, () =>
			{
				Sync.IsUpgradeableReadLockHeld.Should().BeFalse();
				return true;
			}, Run).Should().BeFalse();
			Sync.Handler().TryWriteConditional(1, ref result, hasLock =>
			{
				Sync.IsUpgradeableReadLockHeld.Should().Be(hasLock);
				return true;
			}, Run).Should().BeFalse();
			ran.Should().BeFalse();
			result.Should().BeFalse();
		});

		Sync.TryWriteConditional(1000, ref result, () =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().BeTrue();
			return true;
		}, Run).Should().BeTrue();
		ran.Should().BeTrue();
		result.Should().BeTrue();

		result = ran = false;
		Sync.Handler().TryWriteConditional(1000, ref result, () =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().BeTrue();
			return true;
		}, Run).Should().BeTrue();
		ran.Should().BeTrue();
		result.Should().BeTrue();

		result = ran = false;
		Sync.TryWriteConditional(1000, ref result, hasLock =>
		{
			hasLock.Should().BeFalse();
			Sync.IsUpgradeableReadLockHeld.Should().BeFalse();
			return false;
		}, Run).Should().BeFalse();
		ran.Should().BeFalse();
		result.Should().BeFalse();

		Sync.TryWriteConditional(1000, ref result, hasLock =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().Be(hasLock);
			return !hasLock;
		}, Run).Should().BeFalse();
		ran.Should().BeFalse();
		result.Should().BeFalse();

		Sync.TryWriteConditional(1000, ref result, hasLock =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().Be(hasLock);
			return true;
		}, Run).Should().BeTrue();
		ran.Should().BeTrue();
		result.Should().BeTrue();

		result = ran = false;
		Sync.Handler().TryWriteConditional(1000, ref result, hasLock =>
		{
			hasLock.Should().BeFalse();
			Sync.IsUpgradeableReadLockHeld.Should().BeFalse();
			return false;
		}, Run).Should().BeFalse();
		ran.Should().BeFalse();
		result.Should().BeFalse();

		Sync.Handler().TryWriteConditional(1000, ref result, hasLock =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().Be(hasLock);
			return !hasLock;
		}, Run).Should().BeFalse();
		ran.Should().BeFalse();
		result.Should().BeFalse();

		Sync.Handler().TryWriteConditional(1000, ref result, hasLock =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().Be(hasLock);
			return true;
		}, Run).Should().BeTrue();
		ran.Should().BeTrue();
		result.Should().BeTrue();
	}
}
