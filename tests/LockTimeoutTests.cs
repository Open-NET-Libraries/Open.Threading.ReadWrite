using FluentAssertions;
using Xunit;

namespace Open.Threading.ReadWrite.Tests;

public static class LockTimeoutTests
{
	[Fact]
	public static void InfiniteTest()
	{
		LockTimeout timeout = Timeout.Infinite;
		timeout.IsFinite.Should().BeFalse();
		timeout.IsInfinite.Should().BeTrue();

		timeout = -1L;
		timeout.IsFinite.Should().BeFalse();
		timeout.IsInfinite.Should().BeTrue();

		timeout = -1d;
		timeout.IsFinite.Should().BeFalse();
		timeout.IsInfinite.Should().BeTrue();

		timeout = TimeSpan.FromMilliseconds(-1);
		timeout.IsFinite.Should().BeFalse();
		timeout.IsInfinite.Should().BeTrue();
	}

	[Fact]
	public static void FiniteTest()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new LockTimeout(-2));
		LockTimeout timeout = 10;
		timeout.IsFinite.Should().BeTrue();
		timeout.IsInfinite.Should().BeFalse();

		Assert.Throws<ArgumentOutOfRangeException>(() => new LockTimeout(-2L));
		Assert.Throws<ArgumentOutOfRangeException>(() => new LockTimeout(1L + int.MaxValue));
		timeout = 10L;
		timeout.IsFinite.Should().BeTrue();
		timeout.IsInfinite.Should().BeFalse();

		Assert.Throws<ArgumentOutOfRangeException>(() => new LockTimeout(-2d));
		Assert.Throws<ArgumentOutOfRangeException>(() => new LockTimeout(1d + int.MaxValue));
		timeout = 10d;
		timeout.IsFinite.Should().BeTrue();
		timeout.IsInfinite.Should().BeFalse();

		Assert.Throws<ArgumentOutOfRangeException>(() => new LockTimeout(TimeSpan.FromMilliseconds(-2)));
		Assert.Throws<ArgumentOutOfRangeException>(() => new LockTimeout(TimeSpan.FromMilliseconds(1d + int.MaxValue)));
		timeout = TimeSpan.FromMilliseconds(10);
		timeout.IsFinite.Should().BeTrue();
		timeout.IsInfinite.Should().BeFalse();
	}
}
