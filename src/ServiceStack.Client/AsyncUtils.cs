﻿// Copyright (c) Service Stack LLC. All Rights Reserved.
// License: https://raw.github.com/ServiceStack/ServiceStack/master/license.txt

/*
 * Keep as much platform specific stuff here
 */

using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

#if NETFX_CORE
using Windows.System.Threading;
#endif

namespace ServiceStack
{
    public interface ITimer : IDisposable
    {
        void Cancel();
    }

    public delegate void ProgressDelegate(long done, long total);

    internal static class AsyncUtils
    {
        public static Exception CreateTimeoutException(this Exception ex, string errorMsg)
        {
#if SL5 || PCL
            return new WebException("The request timed out", ex, WebExceptionStatus.RequestCanceled, null);
#else
            return new WebException("The request timed out", ex, WebExceptionStatus.Timeout, null);
#endif
        }

        internal static ITimer CreateTimer<TResponse>(this AsyncState<TResponse> state, TimeSpan timeOut)
        {
            return PclExportClient.Instance.CreateTimer(state, timeOut);
        }

        internal static void EndReadStream(this Stream stream)
        {
#if NETFX_CORE || WP
            stream.Dispose();
#else
            stream.Close();
#endif
        }

        internal static void EndWriteStream(this Stream stream)
        {
#if NETFX_CORE || WP
                stream.Flush();
                stream.Dispose();
#else
            stream.Close();
#endif
        }

        internal static HttpWebRequest CreateHttpWebRequest(this AsyncServiceClient client, string requestUri)
        {
#if SL5

            var creator = client.EmulateHttpViaPost
                ? System.Net.Browser.WebRequestCreator.BrowserHttp
                : System.Net.Browser.WebRequestCreator.ClientHttp;

            var webRequest = (HttpWebRequest) creator.Create(new Uri(requestUri));

            if (client.StoreCookies && !client.EmulateHttpViaPost)
            {
                if (client.ShareCookiesWithBrowser)
                {
                    if (client.CookieContainer == null)
                        client.CookieContainer = new CookieContainer();
                    client.CookieContainer.SetCookies(new Uri(requestUri), System.Windows.Browser.HtmlPage.Document.Cookies);
                }
                
                webRequest.CookieContainer = client.CookieContainer;	
            }

#else
            var webRequest = (HttpWebRequest)WebRequest.Create(requestUri);
            PclExport.Instance.Config(webRequest);
            client.CancelAsyncFn = webRequest.Abort;

            if (client.StoreCookies)
            {
                webRequest.CookieContainer = client.CookieContainer;
            }
#endif

#if !SL5
            if (!client.DisableAutoCompression)
            {
                PclExport.Instance.AddCompression(webRequest);
            }
#endif
            return webRequest;
        }

        public static void SynchronizeCookies(this AsyncServiceClient client)
        {
#if SL5
            if (client.StoreCookies && client.ShareCookiesWithBrowser && !client.EmulateHttpViaPost)
            {
                // browser cookies must be set on the ui thread
                System.Windows.Deployment.Current.Dispatcher.BeginInvoke(() => {
                    var cookieHeader = client.CookieContainer.GetCookieHeader(new Uri(client.BaseUri));
                    System.Windows.Browser.HtmlPage.Document.Cookies = cookieHeader;
                });
            }
#endif
        }

        public static bool IsWebException(this WebException webEx)
        {
            return webEx != null && webEx.Response != null
#if !(SL5 || PCL)
                && webEx.Status == WebExceptionStatus.ProtocolError
#endif
            ;
        }

        public static void ResetStream(this Stream stream)
        {
#if !IOS
            // MonoTouch throws NotSupportedException when setting System.Net.WebConnectionStream.Position
            // Not sure if the stream is used later though, so may have to copy to MemoryStream and
            // pass that around instead after this point?
            stream.Position = 0;
#endif
        }
    }

#if !NET45
    internal class TaskConstants<T>
    {
        internal static readonly Task<T> Canceled;

        static TaskConstants()
        {
            var tcs = new TaskCompletionSource<T>();
            tcs.SetCanceled();
            Canceled = tcs.Task;
        }
    }

    internal class TaskConstants
    {
        public static readonly Task Finished;
        public static readonly Task Canceled;

        static TaskConstants()
        {
            var tcs = new TaskCompletionSource<object>();
            tcs.SetResult(null);
            Finished = tcs.Task;

            tcs = new TaskCompletionSource<object>();
            tcs.SetCanceled();
            Canceled = tcs.Task;
        }
    }

    internal static class AsyncNet45StreamExtensions
    {
        public static Task FlushAsync(this Stream stream)
        {
            return stream.FlushAsync(CancellationToken.None);
        }

        public static Task FlushAsync(this Stream stream, CancellationToken token)
        {
            return token.IsCancellationRequested
                ? TaskConstants.Canceled
                : Task.Factory.StartNew(l => ((Stream)l).Flush(), stream, token);
        }

        public static Task<int> ReadAsync(this Stream stream, byte[] buffer, int offset, int count)
        {
            return stream.ReadAsync(buffer, offset, count, CancellationToken.None);
        }

        public static Task WriteAsync(this Stream stream, byte[] buffer)
        {
            return stream.WriteAsync(buffer, 0, buffer.Length, CancellationToken.None);
        }

        public static Task WriteAsync(this Stream stream, byte[] buffer, int offset, int count)
        {
            return stream.WriteAsync(buffer, offset, count, CancellationToken.None);
        }

#if !PCL
        public static Task<int> ReadAsync(this Stream stream, byte[] buffer, int offset, int count, CancellationToken token)
        {
            return token.IsCancellationRequested
                ? TaskConstants<int>.Canceled
                : Task<int>.Factory.FromAsync(stream.BeginRead, stream.EndRead, buffer, offset, count, null);
        }

        public static Task WriteAsync(this Stream stream, byte[] buffer, int offset, int count, CancellationToken token)
        {
            return Task.Factory.FromAsync(stream.BeginWrite, stream.EndWrite, buffer, offset, count, null);
        }
#endif

    }
#endif

    public static class TaskExtensions
    {
        public static Task<T> Success<T>(this Task<T> task, Action<T> fn,
            TaskContinuationOptions taskOptions = TaskContinuationOptions.OnlyOnRanToCompletion)
        {
            task.ContinueWith(t => fn(t.Result), TaskScheduler.FromCurrentSynchronizationContext());
            return task;
        }

        public static Task<T> Error<T>(this Task<T> task, Action<Exception> fn,
            TaskContinuationOptions taskOptions = TaskContinuationOptions.NotOnRanToCompletion)
        {
            task.ContinueWith(t => fn(t.Exception), TaskScheduler.FromCurrentSynchronizationContext());
            return task;
        }
    }

}