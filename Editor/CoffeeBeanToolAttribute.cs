using System;

namespace CoffeeBean.EditorTools
{
    /// <summary>
    /// CoffeeBean 工具窗口标记：各模块的 Editor 工具窗口类打上此标记后，
    /// 会被 CoffeeBean Hub 窗口（Window &gt; CoffeeBean）自动发现并列出入口。
    ///
    /// 解耦说明：为保持模块独立（excel/purchase 等不编译期依赖 core），
    /// 各模块在自己的 Editor 程序集中复制一个**同命名空间同名**的
    /// CoffeeBeanToolAttribute 类（几行），Hub 用反射按全名匹配识别，
    /// 无需模块引用 core 程序集。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class CoffeeBeanToolAttribute : Attribute
    {
        /// <summary>工具标题（Hub 导航列表显示）。</summary>
        public string Title { get; }

        /// <summary>工具描述（Hub 中悬停/副标题显示）。</summary>
        public string Description { get; }

        /// <summary>所属模块显示名（分组用，如 "Excel" / "Purchase"）。</summary>
        public string Module { get; }

        public CoffeeBeanToolAttribute(string title, string description = "", string module = "")
        {
            Title = title;
            Description = description;
            Module = module;
        }
    }
}
