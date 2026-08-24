using System;
using System.Collections.Generic;

namespace CoffeeBean
{
    /// <summary>
    /// 轻量服务注册表：模块在 OnLoad 时按接口类型注册服务，其他模块通过接口获取，
    /// 从而避免模块之间产生程序集级横向依赖。
    /// </summary>
    public sealed class ServiceRegistry
    {
        private readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();

        public void Register<T>(T instance) where T : class
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            Register(typeof(T), instance);
        }

        public void Register(Type type, object instance)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            if (!type.IsInstanceOfType(instance))
                throw new ArgumentException(
                    $"Instance of type {instance.GetType().Name} is not assignable to {type.Name}", nameof(instance));
            _services[type] = instance;
        }

        public bool TryGet<T>(out T service) where T : class
        {
            if (_services.TryGetValue(typeof(T), out object obj))
            {
                service = (T)obj;
                return true;
            }
            service = null;
            return false;
        }

        /// <summary>获取服务；未注册时抛出异常。</summary>
        public T Get<T>() where T : class
        {
            if (!TryGet(out T service))
                throw new InvalidOperationException($"Service of type {typeof(T).Name} is not registered.");
            return service;
        }

        public bool Unregister<T>() where T : class => _services.Remove(typeof(T));

        public void Clear() => _services.Clear();
    }
}
