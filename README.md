# Open.Threading.ReadWrite

Useful set of extensions and classes for simplifying and optimizing read-write synchronization with `ReaderWriterLockSlim`.

[![GitHub license](https://img.shields.io/badge/license-MIT-blue.svg?style=flat-square)](https://www.nuget.org/packages/Open.Threading.ReadWrite/blob/master/LICENSE)
![100% code coverage](https://img.shields.io/badge/coverage-100%25-green)
[![NuGet](https://img.shields.io/nuget/v/Open.Threading.ReadWrite.svg)](https://www.nuget.org/packages/Open.Threading.ReadWrite/)

## Purpose

There are very common but tedious patterns to follow when utilizing a `ReaderWriterLockSlim`. Some of the more complex but useful patterns involve reading (with or without a read lock) and then if a condition is met, acquiring a write lock before proceeding.

This extension library removes the tediousness of properly acquiring and releasing various locks and provides easy access to timeout values if needed.

## Basics

---

### Read

```cs
rwLockSlim.Read(()=>
{
    /* do work inside a read lock */
});
```

```cs
using(rwLockSlim.ReadLock())
{
    /* do work inside a read lock */
}
```

```cs
int result = rwLockSlim.Read(()=>
{
    int i;
    /* produce a result inside read lock */
    return i;
});
```

---

### Write

```cs
rwLockSlim.Write(()=>
{
    /* do work inside a read lock */
});
```

```cs
using(rwLockSlim.WriteLock())
{
    /* do work inside a write lock */
}
```

```cs
int result = rwLockSlim.Write(()=>
{
    int i;
    /* produce a result inside write lock */
    return i;
});
```

---

### Upgradable Read

```cs
using(rwLockSlim.UpgradableReadLock())
{
    /* do work inside an upgradable read lock */
    if(condition)
    {
        using(rwLockSlim.WriteLock())
        {
            /* upgraded to a write lock */
        }
    }
}
```


## With Timeouts

These throw a `TimeoutException` if a lock cannot be acquired within the time specified. 

---

### Read

```cs
rwLockSlim.Read(1000 /* ms */, ()=>
{
    /* do work inside a read lock */
});
```

or

```cs
using(rwLockSlim.ReadLock(1000)) // ms
{
    /* do work inside a read lock */
}
```

---

### Write

```cs
rwLockSlim.Write(1000 /* ms */, ()=>
{
    /* do work inside a write lock */
});
```

or

```cs
using(rwLockSlim.WriteLock(1000)) // ms
{
    /* do work inside a write lock */
}
```

## Advanced Examples

---

### WriteConditional

This example demonstrates how to properly query a value before writing with a 1 second timeout.

```cs
var actionWasInvoked = rwLockSlim.WriteConditional(1000 /* ms */,
()=> /* condition that is queried inside an upgradable read lock */,
()=> /* do work inside a write lock */);
```

### ReadWriteConditional

This more advanced example optimizes the process of reading and then writing by first testing the condition within a read lock before attempting with an upgradable read lock.

```cs
int result = 0;
bool actionWasInvoked = rwLockSlim.ReadWriteConditional(ref result,
isUpgraded => /* condition that is first queried inside a read lock */,
()=>
{
    int i;
    /* do work inside a write lock */
    return i;
});
```