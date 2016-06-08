﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Routing;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Routing
{
    public class ConfiguredLocalRoutingSpec : AkkaSpec
    {
        public ConfiguredLocalRoutingSpec() :base(GetConfig())   
        {
        }

        private static string GetConfig()
        {
            return @"
                akka {
                  actor {
                    default-dispatcher {
                        type = ForkJoinDispatcher
                        throughput = 100
                        dedicated-thread-pool {
                            thread-count = 4
                            deadlock-timeout = 3s
                            threadtype = background
                        }
                    }
                    deployment {
                      /config {
                        router = random-pool
                        nr-of-instances = 4
                        pool-dispatcher = ${akka.actor.default-dispatcher}
                      }
                      /paths {
                        router = random-group
                        routees.paths = [""/user/service1"", ""/user/service2""]
                      }
                      /weird {
                        router = round-robin-pool
                        nr-of-instances = 3
                      }
                      ""/weird/*"" {
                        router = round-robin-pool
                        nr-of-instances = 2
                      }
                      /sys-parent/round {
                        router = round-robin-pool
                        nr-of-instances = 6
                      }
                    }
                  }
                }
            ";
        }

        private class EchoProps : ReceiveActor
        {
            public EchoProps()
            {
                ReceiveAny(c => Sender.Tell(Context.Props));
            }
        }

        private class EchoActor : ReceiveActor
        {
            public EchoActor()
            {
                ReceiveAny(message => Sender.Tell(message));
            }
        }

        private class SendRefAtStartup : ReceiveActor
        {
            public SendRefAtStartup(IActorRef testActor)
            {
                testActor.Tell(Self);
                ReceiveAny(c => 
                {
                    
                });
            }
        }

        private class PropsName
        {
            public PropsName(Props props, string name)
            {
                Props = props;
                Name = name;
            }

            public Props Props { get; }

            public string Name { get; }
        }

        private class Parent : ReceiveActor
        {
            public Parent()
            {
                Receive<PropsName>(c =>
                {
                    Sender.Tell(Context.ActorOf(c.Props, c.Name));
                });
            }
        }

        private RouterConfig RouterConfig(IActorRef actor)
        {
            var routedActor = (RoutedActorRef)actor;
            if (routedActor != null)
            {
                var routedActorCell = (RoutedActorCell)routedActor.Underlying;
                if (routedActorCell != null)
                {
                    return routedActorCell.RouterConfig;
                }
                else
                {
                    AwaitCondition(() => routedActor.IsStarted, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10));
                    return RouterConfig(actor);
                }
            }

            return null;
        }

        [Fact]
        public async Task RouterConfig_must_be_picked_up_from_Props()
        {
            var actor = Sys.ActorOf(new RoundRobinPool(12).Props(Props.Create<EchoProps>()), "someOther");
            var routerConfig = RouterConfig(actor);

            routerConfig.GetType().ShouldBe(typeof(RoundRobinPool));
            var roundRobinPool = (RoundRobinPool)routerConfig;
            roundRobinPool.NrOfInstances.ShouldBe(12);

            var result = await actor.GracefulStop(TimeSpan.FromSeconds(3));
            Assert.True(result);
        }

        [Fact]
        public async Task RouterConfig_must_be_overridable_in_config()
        {
            var actor = Sys.ActorOf(new RoundRobinPool(12).Props(Props.Create<EchoProps>()), "config");
            var routerConfig = RouterConfig(actor);

            routerConfig.GetType().ShouldBe(typeof(RandomPool));
            var randomPool = (RandomPool)routerConfig;
            randomPool.NrOfInstances.ShouldBe(4);
            randomPool.UsePoolDispatcher.ShouldBe(true);

            var result = await actor.GracefulStop(TimeSpan.FromSeconds(3));
            Assert.True(result);
        }

        [Fact]
        public async Task RouterConfig_must_use_routeesPaths_from_config()
        {
            var actor = Sys.ActorOf(new RandomPool(12).Props(Props.Create<EchoProps>()), "paths");
            var routerConfig = RouterConfig(actor);

            routerConfig.GetType().ShouldBe(typeof(RandomGroup));
            var randomGroup = (RandomGroup)routerConfig;
            randomGroup.Paths.ShouldAllBeEquivalentTo(new List<string> { "/user/service1", "/user/service2" });

            var result = await actor.GracefulStop(TimeSpan.FromSeconds(3));
            Assert.True(result);
        }

        [Fact]
        public async Task RouterConfig_must_be_overridable_in_explicit_deployment()
        {
            var actor = Sys.ActorOf(FromConfig.Props(Props.Create<EchoProps>()).WithDeploy(new Deploy(new RoundRobinPool(12))), "someOther");
            var routerConfig = RouterConfig(actor);

            routerConfig.GetType().ShouldBe(typeof(RoundRobinPool));
            var roundRobinPool = (RoundRobinPool)routerConfig;
            roundRobinPool.NrOfInstances.ShouldBe(12);

            var result = await actor.GracefulStop(TimeSpan.FromSeconds(3));
            Assert.True(result);
        }

        [Fact]
        public async Task RouterConfig_must_be_overridable_in_config_even_with_explicit_deployment()
        {
            var actor = Sys.ActorOf(FromConfig.Props(Props.Create<EchoProps>()).WithDeploy(new Deploy(new RoundRobinPool(12))), "config");
            var routerConfig = RouterConfig(actor);

            routerConfig.GetType().ShouldBe(typeof(RandomPool));
            var randomPool = (RandomPool)routerConfig;
            randomPool.NrOfInstances.ShouldBe(4);
            randomPool.UsePoolDispatcher.ShouldBe(true);

            var result = await actor.GracefulStop(TimeSpan.FromSeconds(3));
            Assert.True(result);
        }

        [Fact]
        public void RouterConfig_must_be_fail_with_exception_if_not_correct()
        {
            Intercept<ConfigurationException>(() =>
            {
                Sys.ActorOf(FromConfig.Props());
            });
        }

        [Fact(Skip = "Wrong actors names")]
        public void RouterConfig_must_not_get_confused_when_trying_to_wildcard_configure_children()
        {
            var router = Sys.ActorOf(FromConfig.Props(Props.Create<SendRefAtStartup>(TestActor)), "weird");

            var received = Enumerable.Range(1, 3).Select(_ => ExpectMsg<IActorRef>()).ToList();
            var expected = new List<string> {"a", "b", "c"}.Select(i => Sys.ActorSelection("/user/weird/$" + i).ResolveOne(RemainingOrDefault).Result).ToList();

            received.ShouldAllBeEquivalentTo(expected);
            ExpectNoMsg(TimeSpan.FromSeconds(1));
        }

        [Fact(Skip = "Not implemented yet")]
        public void RouterConfig_must_upport_custom_router()
        {
            var myRouter = Sys.ActorOf(FromConfig.Props(), "myrouter");
            myRouter.Tell("foo");
            ExpectMsg("bar");
        }

        [Fact(Skip = "SystemActors DSN has not implemented yet")]
        public void RouterConfig_must_load_settings_from_config_for_local_child_router_of_system_actor()
        {
            var probe = CreateTestProbe();
            var parent = Sys.AsInstanceOf<ExtendedActorSystem>().SystemActorOf(Props.Create<Parent>(), "sys-parent");
            parent.Tell(new PropsName(Props.Create<EchoActor>(), "round"), probe.Ref);

            var router = probe.ExpectMsg<IActorRef>();

            var replies = new List<ActorPath>();
            for (int i = 0; i < 10; i++)
            {
                var msg = i.ToString();
                router.Tell(msg, probe.Ref);
                probe.ExpectMsg(msg);
                replies.Add(probe.LastSender.Path);
            }

            var children = new HashSet<ActorPath>(replies);

            children.Count.ShouldBe(6);
            Sys.Stop(router);
        }
    }
}