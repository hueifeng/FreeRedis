﻿using Microsoft.AspNetCore.Connections;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace console_netcore31_newsocket
{

    public class NewRedisClient4
    {

        private readonly static Func<Task<bool>, bool, bool> _setResult;
        private readonly static Func<Task<bool>> _getTask;
        static NewRedisClient4()
        {
            _setResult = typeof(Task<bool>)
                .GetMethod("TrySetResult",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new Type[] { typeof(bool) }, null)
                .CreateDelegate<Func<Task<bool>, bool, bool>>();


            var ctor = typeof(Task<bool>).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new Type[0], null);

            DynamicMethod dynamicMethod = new DynamicMethod("GETTASK", typeof(Task<bool>), new Type[0]);
            var iLGenerator = dynamicMethod.GetILGenerator();
            iLGenerator.Emit(OpCodes.Newobj, ctor);
            iLGenerator.Emit(OpCodes.Ret);
            _getTask = (Func<Task<bool>>)dynamicMethod.CreateDelegate(typeof(Func<Task<bool>>));
        }

        private readonly SourceConcurrentQueue2<Task<bool>> _receiverQueue;
        private readonly byte _protocalStart;
        private readonly ConnectionContext _connection;
        public readonly PipeWriter _sender;
        private readonly PipeReader _reciver;


        public NewRedisClient4(string ip, int port) : this(new IPEndPoint(IPAddress.Parse(ip), port))
        {
        }
        public NewRedisClient4(IPEndPoint point)
        {
            _protocalStart = (byte)43;
            SocketConnectionFactory client = new SocketConnectionFactory(new SocketTransportOptions());
            _connection = client.ConnectAsync(point).Result;
            _sender = _connection.Transport.Output;
            _reciver = _connection.Transport.Input;
            _receiverQueue = new SourceConcurrentQueue2<Task<bool>>(_sender);
            RunReciver();
        }

        private Task<bool> _sendTask;
        public Task<bool> FlushDBAsync()
        {
            var bytes = Encoding.UTF8.GetBytes($"flushdb\r\n");
            var taskSource = _getTask();
            _receiverQueue.Enqueue(taskSource, bytes);
            return taskSource;
        }
        public Task<bool> SetAsync(string key, string value)
        {
            var bytes = Encoding.UTF8.GetBytes($"SET {key} {value}\r\n");
            var taskSource = _getTask();
            _receiverQueue.Enqueue(taskSource, bytes);
            return taskSource;
        }
        private readonly object _lock = new object();
        private int _taskCount;
        long total = 0;

        private async void RunReciver()
        {

            while (true)
            {

                var result = await _reciver.ReadAsync().ConfigureAwait(false);
                var buffer = result.Buffer;

                //if (buffer.IsSingleSegment)
                //{

                    //total += buffer.Length;
                    //Console.WriteLine($"当前剩余 {_taskCount} 个任务未完成,队列中有 {_receiverQueue.Count} 个任务！缓冲区长 {buffer.Length} .");
                    Handler(buffer);
                //}
               // else
                //{

                    //total += buffer.Length;
                    //Console.WriteLine($"当前剩余 {_taskCount} 个任务未完成,队列中有 {_receiverQueue.Count} 个任务！缓冲区长 {buffer.Length} .");
                ///    Handler(buffer);
                //}
                _reciver.AdvanceTo(buffer.End);
                if (result.IsCompleted)
                {
                    return;
                }

            }
        }


        private void Handler(in ReadOnlySequence<byte> sequence)
        {
            Task<bool> task;
            var reader = new SequenceReader<byte>(sequence);

            while (reader.TryReadTo(out ReadOnlySpan<byte> _, 43, advancePastDelimiter: true))
            {

                while (!_receiverQueue.TryDequeue(out task)) { }

                TrySetResult(task, true);
            }

        }

        public bool TrySetResult(Task<bool> task, bool result)
        {
            bool rval = _setResult(task, result);
            if (!rval)
            {
                SpinWait sw = default;
                while (!task.IsCompleted)
                {
                    sw.SpinOnce();
                }
            }

            return rval;
        }

    }
}
