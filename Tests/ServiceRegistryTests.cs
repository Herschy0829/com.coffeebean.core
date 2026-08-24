using System;
using CoffeeBean;
using NUnit.Framework;

namespace CoffeeBean.Tests
{
    public class ServiceRegistryTests
    {
        private interface IFoo { }

        private sealed class Foo : IFoo { }

        [Test]
        public void RegisterAndGet_ReturnsSameInstance()
        {
            var registry = new ServiceRegistry();
            var foo = new Foo();
            registry.Register<IFoo>(foo);
            Assert.AreSame(foo, registry.Get<IFoo>());
        }

        [Test]
        public void TryGet_Missing_ReturnsFalse()
        {
            var registry = new ServiceRegistry();
            Assert.IsFalse(registry.TryGet<IFoo>(out _));
        }

        [Test]
        public void Get_Missing_Throws()
        {
            var registry = new ServiceRegistry();
            Assert.Throws<InvalidOperationException>(() => registry.Get<IFoo>());
        }

        [Test]
        public void Register_WrongType_Throws()
        {
            var registry = new ServiceRegistry();
            Assert.Throws<ArgumentException>(() => registry.Register(typeof(IFoo), new object()));
        }

        [Test]
        public void Unregister_RemovesService()
        {
            var registry = new ServiceRegistry();
            registry.Register<IFoo>(new Foo());
            Assert.IsTrue(registry.Unregister<IFoo>());
            Assert.IsFalse(registry.TryGet<IFoo>(out _));
        }
    }
}
