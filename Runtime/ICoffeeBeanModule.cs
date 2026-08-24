namespace CoffeeBean
{
    /// <summary>
    /// CoffeeBean 模块生命周期接口（可选实现）。
    /// 未实现此接口的模块仅参与注册与依赖检查，不接收生命周期回调。
    /// </summary>
    public interface ICoffeeBeanModule
    {
        /// <summary>依赖模块全部 OnLoad 之后、按拓扑顺序调用；在此向 context.Services 注册服务。</summary>
        void OnLoad(CoffeeBeanContext context);

        /// <summary>所有模块 OnLoad 完成后调用。</summary>
        void OnStart();

        /// <summary>退出时按依赖反序调用。</summary>
        void OnShutdown();
    }
}
