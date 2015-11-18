using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using DryIoc.UnitTests.CUT;
using NUnit.Framework;

namespace DryIoc.UnitTests
{
    [TestFixture]
    public class RulesTests
    {
        [Test]
        public void Given_service_with_two_ctors_I_can_specify_what_ctor_to_choose_for_resolve()
        {
            var container = new Container();

            container.Register(typeof(Bla<>), made: Made.Of(
                t => t.GetConstructorOrNull(args: new[] { typeof(Func<>).MakeGenericType(t.GetGenericParamsAndArgs()[0]) })));

            container.Register(typeof(SomeService), typeof(SomeService));

            var bla = container.Resolve<Bla<SomeService>>();

            Assert.That(bla.Factory(), Is.InstanceOf<SomeService>());
        }

        [Test]
        public void I_should_be_able_to_add_rule_to_resolve_not_registered_service()
        {
            var container = new Container(Rules.Default.WithUnknownServiceResolvers(request =>
                !request.ServiceType.IsValueType() && !request.ServiceType.IsAbstract()
                    ? new ReflectionFactory(request.ServiceType)
                    : null));

            var service = container.Resolve<NotRegisteredService>();

            Assert.That(service, Is.Not.Null);
        }

        [Test]
        public void I_can_remove_rule_to_resolve_not_registered_service()
        {
            Rules.UnknownServiceResolver unknownServiceResolver = request =>
                !request.ServiceType.IsValueType() && !request.ServiceType.IsAbstract()
                    ? new ReflectionFactory(request.ServiceType)
                    : null;
            
            IContainer container = new Container(Rules.Default.WithUnknownServiceResolvers(unknownServiceResolver));
            Assert.NotNull(container.Resolve<NotRegisteredService>());

            container = container
                .With(rules => rules.WithoutUnknownServiceResolver(unknownServiceResolver))
                .WithoutCache(); // Important to remove cache

            Assert.Null(container.Resolve<NotRegisteredService>(IfUnresolved.ReturnDefault));
        }
        
        [Test]
        public void When_service_registered_with_name_Then_it_could_be_resolved_with_ctor_parameter_ImportAttribute()
        {
            var container = new Container(rules => rules.With(parameters: GetServiceInfoFromImportAttribute));

            container.Register(typeof(INamedService), typeof(NamedService));
            container.Register(typeof(INamedService), typeof(AnotherNamedService), serviceKey: "blah");
            container.Register(typeof(ServiceWithImportedCtorParameter));

            var service = container.Resolve<ServiceWithImportedCtorParameter>();

            Assert.That(service.NamedDependency, Is.InstanceOf<AnotherNamedService>());
        }

        [Test]
        public void I_should_be_able_to_import_single_service_based_on_specified_metadata()
        {
            var container = new Container(rules => rules.With(parameters: GetServiceFromWithMetadataAttribute));

            container.Register(typeof(IFooService), typeof(FooHey), setup: Setup.With(metadataOrFuncOfMetadata: FooMetadata.Hey));
            container.Register(typeof(IFooService), typeof(FooBlah), setup: Setup.With(metadataOrFuncOfMetadata: FooMetadata.Blah));
            container.Register(typeof(FooConsumer));

            var service = container.Resolve<FooConsumer>();

            Assert.That(service.Foo.Value, Is.InstanceOf<FooBlah>());
        }

        [Test]
        public void You_can_specify_rules_to_resolve_last_registration_from_multiple_available()
        {
            var container = new Container(Rules.Default.WithFactorySelector(Rules.SelectLastRegisteredFactory()));

            container.Register(typeof(IService), typeof(Service));
            container.Register(typeof(IService), typeof(AnotherService));
            var service = container.Resolve(typeof(IService));

            Assert.That(service, Is.InstanceOf<AnotherService>());
        }

        [Test]
        public void You_can_specify_rules_to_disable_registration_based_on_reuse_type()
        {
            var container = new Container(Rules.Default.WithFactorySelector(
                (request, factories) => factories.FirstOrDefault(f => f.Key.Equals(request.ServiceKey) && !(f.Value.Reuse is SingletonReuse)).Value));

            container.Register<IService, Service>(Reuse.Singleton);
            var service = container.Resolve(typeof(IService), IfUnresolved.ReturnDefault);

            Assert.That(service, Is.Null);
        }

        public static Func<ParameterInfo, ParameterServiceInfo> GetServiceInfoFromImportAttribute(Request request)
        {
            return parameter =>
            {
                var import = (ImportAttribute)parameter.GetAttributes(typeof(ImportAttribute)).FirstOrDefault();
                var details = import == null ? ServiceDetails.Default
                    : ServiceDetails.Of(import.ContractType, import.ContractName);
                return ParameterServiceInfo.Of(parameter).WithDetails(details, request);
            };
        }

        public static Func<ParameterInfo, ParameterServiceInfo> GetServiceFromWithMetadataAttribute(Request request)
        {
            return parameter =>
            {
                var import = (ImportWithMetadataAttribute)parameter.GetAttributes(typeof(ImportWithMetadataAttribute))
                    .FirstOrDefault();
                if (import == null)
                    return null;

                var registry = request.Container;
                var serviceType = parameter.ParameterType;
                serviceType = registry.GetWrappedType(serviceType, request.RequiredServiceType);
                var metadata = import.Metadata;
                var factory = registry.GetAllServiceFactories(serviceType)
                    .FirstOrDefault(kv => metadata.Equals(kv.Value.Setup.Metadata))
                    .ThrowIfNull();

                return ParameterServiceInfo.Of(parameter)
                    .WithDetails(ServiceDetails.Of(serviceType, factory.Key), request);
            };
        }

        [Test]
        public void Can_turn_Off_singleton_optimization()
        {
            var container = new Container(r => r.WithoutEagerCachingSingletonForFasterAccess());
            container.Register<FooHey>(Reuse.Singleton);

            var singleton = container.Resolve<LambdaExpression>(typeof(FooHey));

            Assert.That(singleton.ToString(), Is.StringContaining("SingletonScope"));
        }

        internal class XX { }
        internal class YY { }
        internal class ZZ { }

        [Test]
        public void AutoFallback_resolution_rule_should_respect_IfUnresolved_policy_in_case_of_multiple_registrations()
        {
            var container = new Container()
                .WithAutoFallbackResolution(new[] { typeof(Me), typeof(MiniMe) }, 
                (reuse, request) => reuse == Reuse.Singleton ? null : reuse);

            var me = container.Resolve<IMe>(IfUnresolved.ReturnDefault);

            Assert.IsNull(me);
        }

        [Test] 
        public void AutoFallback_resolution_rule_should_respect_IfUnresolved_policy_in_case_of_multiple_registrations_from_assemblies()
        {
            var container = new Container()
                .WithAutoFallbackResolution(new[] { typeof(Me).GetAssembly() },
                (reuse, request) => reuse == Reuse.Singleton ? null : reuse);

            var me = container.Resolve<IMe>(IfUnresolved.ReturnDefault);

            Assert.IsNull(me);
        }

        [Test]
        public void You_may_specify_condition_to_exclude_unwanted_services_from_AutoFallback_resolution_rule()
        {
            var container = new Container()
                .WithAutoFallbackResolution(new[] { typeof(Me) }, 
                condition: request => request.Parent.ImplementationType.Name.Contains("Green"));

            container.Register<RedMe>();

            Assert.IsNull(container.Resolve<RedMe>(IfUnresolved.ReturnDefault));
        }

        public interface IMe {}
        internal class Me : IMe {}
        internal class MiniMe : IMe {}
        internal class GreenMe { public GreenMe(IMe me) {} }
        internal class RedMe { public RedMe(IMe me) { } }

        [Test]
        public void I_can_support_for_specific_primitive_value_injection_via_container_rule()
        {
            var container = new Container(rules => rules.WithItemToExpressionConverter(
                (item, type) => type == typeof(ConnectionString)
                ? Expression.New(type.GetSingleConstructorOrNull(),
                    Expression.Constant(((ConnectionString)item).Value))
                : null));

            var s = new ConnectionString("aaa");
            container.Register(Made.Of(() => new ConStrUser(Arg.Index<ConnectionString>(0)), r => s));

            var user = container.Resolve<ConStrUser>();
            Assert.AreEqual("aaa", user.S.Value);
        }

        public class ConnectionString
        {
            public string Value;
            public ConnectionString(string value)
            {
                Value = value;
            }
        }

        public class ConStrUser 
        {
            public ConnectionString S { get; set; }
            public ConStrUser(ConnectionString s)
            {
                S = s;
            }
        }

        [Test]
        public void Container_should_throw_on_registering_disposable_transient()
        {
            var container = new Container();

            var ex = Assert.Throws<ContainerException>(() => 
                container.Register<AD>());

            Assert.AreEqual(Error.RegisteredDisposableTransientWontBeDisposedByContainer, ex.Error);
        }

        [Test]
        public void I_can_silence_throw_on_registering_disposable_transient_for_specific_registration()
        {
            var container = new Container();

            Assert.DoesNotThrow(() => 
            container.Register<AD>(setup: Setup.With(allowDisposableTransient: true)));
        }

        [Test]
        public void I_can_silence_throw_on_registering_disposable_transient_for_whole_container()
        {
            var container = new Container(rules => rules.WithoutThrowOnRegisteringDisposableTransient());

            Assert.DoesNotThrow(() => 
            container.Register<AD>());
        }

        class AD : IDisposable
        {
            public void Dispose()
            {
            }
        }

        #region CUT

        public class SomeService { }

        public class Bla<T>
        {
            public string Message { get; set; }
            public Func<T> Factory { get; set; }

            public Bla(string message)
            {
                Message = message;
            }

            public Bla(Func<T> factory)
            {
                Factory = factory;
            }
        }

        enum FooMetadata { Hey, Blah }

        public interface IFooService
        {
        }

        public class FooHey : IFooService
        {
        }

        public class FooBlah : IFooService
        {
        }

        [AttributeUsage(AttributeTargets.Parameter)]
        public class ImportWithMetadataAttribute : Attribute
        {
            public ImportWithMetadataAttribute(object metadata)
            {
                Metadata = metadata.ThrowIfNull();
            }

            public readonly object Metadata;
        }

        public class FooConsumer
        {
            public Lazy<IFooService> Foo { get; set; }

            public FooConsumer([ImportWithMetadata(FooMetadata.Blah)] Lazy<IFooService> foo)
            {
                Foo = foo;
            }
        }

        public class TransientOpenGenericService<T>
        {
            public T Value { get; set; }
        }

        public interface INamedService
        {
        }

        public class NamedService : INamedService
        {
        }

        public class AnotherNamedService : INamedService
        {
        }

        public class ServiceWithImportedCtorParameter
        {
            public INamedService NamedDependency { get; set; }

            public ServiceWithImportedCtorParameter([Import("blah")]INamedService namedDependency)
            {
                NamedDependency = namedDependency;
            }
        }

        // ReSharper disable once ClassNeverInstantiated.Local
        class NotRegisteredService
        {
        }

        #endregion
    }
}