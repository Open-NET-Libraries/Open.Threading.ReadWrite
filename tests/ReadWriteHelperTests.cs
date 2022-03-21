using Xunit;

namespace Open.Threading.ReadWrite.Tests;

public class ReadWriteHelperTests
{
	readonly ReadWriteHelper<object> _helper = new();

	[Theory]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(3)]
	[InlineData("Key1")]
	[InlineData("Key2")]
	[InlineData("Key3")]
	public void ContextTests(object key)
	{
		var rand = new Random();
		Parallel.For(0, 100000, _ =>
		{
			var rwlock = _helper.Context(key);
			switch(rand.Next(3))
			{
				case 0:
					rwlock.Read(() => true);
					break;
				case 1:
					rwlock.Write(() => true);
					break;
				case 2:
					rwlock.ReadUpgradable(rwl =>
					{
						if (rand.Next(1) == 0) return;
						rwl.Write(() => { });
					});
					break;
				case 3:
					rwlock.ReadWriteConditional(
						_ => rand.Next(1)==1,
						() => { });
					break;
			}
		});
	}
}
