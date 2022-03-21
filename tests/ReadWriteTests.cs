using FluentAssertions;
using Xunit;

namespace Open.Threading.ReadWrite.Tests;

public class ReadWriteTests : LockTestBase
{
	[Fact]
	public void ConditionalTest()
	{
		bool ran = false;
		Assert.Throws<ArgumentNullException>(() => Sync.ReadWriteConditional(1000, _ => false, default!));
		Assert.Throws<ArgumentNullException>(() => Sync.ReadWriteConditional(1000, default!, () => { }));

		ran.Should().BeFalse();

		void Run()
		{
			Sync.IsWriteLockHeld.Should().BeTrue();
			ran = true;
		}

		TestTimeout(() =>
		{
			Assert.Throws<TimeoutException>(() => Sync.ReadWriteConditional(1, _ => true, Run));
			ran.Should().BeFalse();
		});

		Sync.ReadWriteConditional(hasLock =>
		{
			hasLock.Should().BeFalse();
			Sync.IsUpgradeableReadLockHeld.Should().BeFalse();
			return false;
		}, Run).Should().BeFalse();
		ran.Should().BeFalse();

		Sync.ReadWriteConditional(hasLock =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().Be(hasLock);
			return !hasLock;
		}, Run).Should().BeFalse();
		ran.Should().BeFalse();

		Sync.ReadWriteConditional(hasLock =>
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
		Assert.Throws<ArgumentNullException>(() => Sync.ReadWriteConditional(1000, ref ran, _ => false, default!));
		Assert.Throws<ArgumentNullException>(() => Sync.ReadWriteConditional(1000, ref ran, default!, () => false));
		Assert.Throws<ArgumentNullException>(() => Sync.Handler().ReadWriteConditional(1000, ref ran, _ => false, default!));
		Assert.Throws<ArgumentNullException>(() => Sync.Handler().ReadWriteConditional(1000, ref ran, default!, () => false));

		ran.Should().BeFalse();

		bool Run()
		{
			Sync.IsWriteLockHeld.Should().BeTrue();
			ran = true;
			return true;
		}

		bool result = false;

		TestTimeout(() =>
		{
			Assert.Throws<TimeoutException>(() => Sync.ReadWriteConditional(1, ref result, _ => true, Run));
			ran.Should().BeFalse();
			result.Should().BeFalse();
		});

		Sync.ReadWriteConditional(ref result, hasLock =>
		{
			hasLock.Should().BeFalse();
			Sync.IsUpgradeableReadLockHeld.Should().BeFalse();
			return false;
		}, Run).Should().BeFalse();
		ran.Should().BeFalse();
		result.Should().BeFalse();

		Sync.ReadWriteConditional(ref result, hasLock =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().Be(hasLock);
			return !hasLock;
		}, Run).Should().BeFalse();
		ran.Should().BeFalse();
		result.Should().BeFalse();

		Sync.ReadWriteConditional(ref result, hasLock =>
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
		Assert.Throws<ArgumentNullException>(() => Sync.TryReadWriteConditional(1000, _ => false, default!));
		Assert.Throws<ArgumentNullException>(() => Sync.TryReadWriteConditional(1000, default!, () => { }));

		ran.Should().BeFalse();

		void Run()
		{
			Sync.IsWriteLockHeld.Should().BeTrue();
			ran = true;
		}

		TestTimeout(() =>
		{
			Sync.TryReadWriteConditional(1, hasLock =>
			{
				Sync.IsUpgradeableReadLockHeld.Should().Be(hasLock);
				return true;
			}, Run).Should().BeFalse();
			ran.Should().BeFalse();

			Sync.Handler().TryReadWriteConditional(1, hasLock =>
			{
				Sync.IsUpgradeableReadLockHeld.Should().Be(hasLock);
				return true;
			}, Run).Should().BeFalse();
			ran.Should().BeFalse();
		});

		Sync.TryReadWriteConditional(1000, hasLock =>
		{
			hasLock.Should().BeFalse();
			Sync.IsUpgradeableReadLockHeld.Should().BeFalse();
			return false;
		}, Run).Should().BeFalse();
		ran.Should().BeFalse();

		Sync.TryReadWriteConditional(1000, hasLock =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().Be(hasLock);
			return !hasLock;
		}, Run).Should().BeFalse();
		ran.Should().BeFalse();

		Sync.TryReadWriteConditional(1000, hasLock =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().Be(hasLock);
			return true;
		}, Run).Should().BeTrue();
		ran.Should().BeTrue();

		ran = false;
		Sync.Handler().TryReadWriteConditional(1000, hasLock =>
		{
			hasLock.Should().BeFalse();
			Sync.IsUpgradeableReadLockHeld.Should().BeFalse();
			return false;
		}, Run).Should().BeFalse();
		ran.Should().BeFalse();

		Sync.Handler().TryReadWriteConditional(1000, hasLock =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().Be(hasLock);
			return !hasLock;
		}, Run).Should().BeFalse();
		ran.Should().BeFalse();

		Sync.Handler().TryReadWriteConditional(1000, hasLock =>
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
		Assert.Throws<ArgumentNullException>(() => Sync.TryReadWriteConditional(1000, ref ran, _ => false, default!));
		Assert.Throws<ArgumentNullException>(() => Sync.TryReadWriteConditional(1000, ref ran, default!, () => false));

		ran.Should().BeFalse();

		bool Run()
		{
			Sync.IsWriteLockHeld.Should().BeTrue();
			ran = true;
			return true;
		}

		var result = false;

		TestTimeout(() =>
		{
			Sync.TryReadWriteConditional(1, ref result, hasLock =>
			{
				Sync.IsUpgradeableReadLockHeld.Should().Be(hasLock);
				return true;
			}, Run).Should().BeFalse();
			ran.Should().BeFalse();
			result.Should().BeFalse();

			Sync.Handler().TryReadWriteConditional(1, ref result, hasLock =>
			{
				Sync.IsUpgradeableReadLockHeld.Should().Be(hasLock);
				return true;
			}, Run).Should().BeFalse();
			ran.Should().BeFalse();
			result.Should().BeFalse();
		});

		Sync.TryReadWriteConditional(1000, ref result, hasLock =>
		{
			hasLock.Should().BeFalse();
			Sync.IsUpgradeableReadLockHeld.Should().BeFalse();
			return false;
		}, Run).Should().BeFalse();
		ran.Should().BeFalse();
		result.Should().BeFalse();

		Sync.TryReadWriteConditional(1000, ref result, hasLock =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().Be(hasLock);
			return !hasLock;
		}, Run).Should().BeFalse();
		ran.Should().BeFalse();
		result.Should().BeFalse();

		Sync.TryReadWriteConditional(1000, ref result, hasLock =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().Be(hasLock);
			return true;
		}, Run).Should().BeTrue();
		ran.Should().BeTrue();
		result.Should().BeTrue();

		ran = result = false;
		Sync.Handler().TryReadWriteConditional(1000, ref result, hasLock =>
		{
			hasLock.Should().BeFalse();
			Sync.IsUpgradeableReadLockHeld.Should().BeFalse();
			return false;
		}, Run).Should().BeFalse();
		ran.Should().BeFalse();
		result.Should().BeFalse();

		Sync.Handler().TryReadWriteConditional(1000, ref result, hasLock =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().Be(hasLock);
			return !hasLock;
		}, Run).Should().BeFalse();
		ran.Should().BeFalse();
		result.Should().BeFalse();

		Sync.Handler().TryReadWriteConditional(1000, ref result, hasLock =>
		{
			Sync.IsUpgradeableReadLockHeld.Should().Be(hasLock);
			return true;
		}, Run).Should().BeTrue();
		ran.Should().BeTrue();
		result.Should().BeTrue();
	}

	[Fact]
	public void GetOrCreateTests()
	{
		Assert.Throws<ArgumentNullException>(() => Sync.GetOrCreateValue<object>(default!, () => default!));
		Assert.Throws<ArgumentNullException>(() => Sync.GetOrCreateValue<object>(() => default!, default!));
		Assert.Throws<ArgumentNullException>(() => Sync.Handler().GetOrCreateValue<object>(default!, () => default!));
		Assert.Throws<ArgumentNullException>(() => Sync.Handler().GetOrCreateValue<object>(() => default!, default!));

		bool created = false;
		object Create()
		{
			created = true;
			return new object();
		}
		var x = new object();
		Sync.GetOrCreateValue(() => x, Create).Should().Be(x);
		created.Should().BeFalse();
		Sync.GetOrCreateValue(() => null, Create).Should().NotBe(x);
		created.Should().BeTrue();

		created = false;
		Sync.Handler().GetOrCreateValue(() => x, Create).Should().Be(x);
		created.Should().BeFalse();
		Sync.Handler().GetOrCreateValue(() => null, Create).Should().NotBe(x);
		created.Should().BeTrue();
	}
}
