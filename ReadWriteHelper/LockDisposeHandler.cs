using System.Diagnostics;

namespace Open.Threading;

readonly struct LockDisposeHandler : IDisposable
{
	private readonly Action onDispose;

	public LockDisposeHandler(ILock iLock, Action onDispose)
	{
		Debug.Assert(iLock is not null);
		Debug.Assert(onDispose is not null);
		Lock = iLock!;
		this.onDispose = onDispose!;
	}

	public ILock Lock { get; }

	public void Dispose()
	{
		Lock.Dispose();
		onDispose();
	}
}
