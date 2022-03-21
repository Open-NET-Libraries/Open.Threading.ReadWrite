namespace Open.Threading.ReadWrite.Tests;

public class LockTestBase
{
	protected readonly ReaderWriterLockSlim Sync;

	protected LockTestBase(ReaderWriterLockSlim sync)
	{
		Sync = sync;
	}

	protected LockTestBase() : this(new()) { }

	protected virtual ILock GetBlockingLock(LockTimeout timeout = default)
		=> new WriteLock(Sync, timeout);

	protected void TestTimeout(Action action)
	{
		using var sync = GetBlockingLock();
		Task.Run(action).Wait();
	}
}