﻿
using Pigeon.SignalR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.App
{
    class Program
    {
        static void Main(string[] args)
        {
            using (var system = ActorSystemSignalR.Create("System A", "http://localhost:8080"))
            {
                var actor = system.GetActor<MyActor>();

                for (int i = 0; i < 1000; i++)
                {
                    actor.Tell(new Greet
                    {
                        Name = "Roger",
                    });
                    actor.Tell(new Greet
                    {
                        Name = "Olle",
                    });
                }

                Console.ReadLine();
            }
        }
    }

    public class Greet : IMessage
    {
        public string Name { get; set; }
    }

    public class LogMessage : IMessage
    {
        public LogMessage(object message)
        {
            this.Timestamp = DateTime.Now;
            this.Message = message;
        }
        public DateTime Timestamp { get;private set; }
        public object Message { get; private set; }
    }

    public class LogActor : TypedActor , IHandle<LogMessage>
    {

        public void Handle(LogMessage message)
        {
            Console.WriteLine("Log {0}", message.Timestamp);
        }
    }

    public class MyActor : UntypedActor
    {
        private ActorRef logger = Context.GetActor<LogActor>();
        protected override void OnReceive(IMessage message)
        {
            //Console.Write(System.Threading.Thread.CurrentThread.GetHashCode());
            message.Match()
                .With<Greet>(m => Console.WriteLine("Hello {0}", m.Name))
                .Default(m => Console.WriteLine("Unknown message {0}",m));

            logger.Tell(new LogMessage(message));
        }
    }
}
