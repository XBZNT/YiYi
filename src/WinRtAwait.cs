using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Windows.Foundation;

namespace YiYi
{
    /// <summary>
    /// 让 WinRT 异步操作支持 await。
    /// 不依赖 System.Runtime.WindowsRuntime.dll（该 DLL 引用了只有 Windows SDK 才有的聚合 Windows.winmd）。
    /// </summary>
    internal static class WinRtAwait
    {
        public static TaskAwaiter<T> GetAwaiter<T>(this IAsyncOperation<T> op)
        {
            TaskCompletionSource<T> tcs = new TaskCompletionSource<T>();
            op.Completed = delegate(IAsyncOperation<T> o, AsyncStatus st)
            {
                if (st == AsyncStatus.Completed) tcs.TrySetResult(o.GetResults());
                else if (st == AsyncStatus.Error) tcs.TrySetException(o.ErrorCode);
                else tcs.TrySetCanceled();
            };
            return tcs.Task.GetAwaiter();
        }

        public static TaskAwaiter GetAwaiter(this IAsyncAction action)
        {
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            action.Completed = delegate(IAsyncAction a, AsyncStatus st)
            {
                if (st == AsyncStatus.Completed) tcs.TrySetResult(true);
                else if (st == AsyncStatus.Error) tcs.TrySetException(a.ErrorCode);
                else tcs.TrySetCanceled();
            };
            return ((Task)tcs.Task).GetAwaiter();
        }

        public static TaskAwaiter GetAwaiter(this Windows.Storage.Streams.DataWriterStoreOperation op)
        {
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            op.Completed = delegate(IAsyncOperation<uint> o, AsyncStatus st)
            {
                if (st == AsyncStatus.Completed) tcs.TrySetResult(true);
                else if (st == AsyncStatus.Error) tcs.TrySetException(o.ErrorCode);
                else tcs.TrySetCanceled();
            };
            return ((Task)tcs.Task).GetAwaiter();
        }
    }
}
