using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace CoffeeBean.EditorTools
{
    /// <summary>
    /// CoffeeBean 工具注册表：反射扫描所有程序集，发现带
    /// <c>CoffeeBean.EditorTools.CoffeeBeanToolAttribute</c>（按全名匹配，含各模块复制的同名定义）
    /// 标记的工具，供 CoffeeBean Hub 窗口列出入口。
    ///
    /// 两类工具（按**结构**区分，不用给 attribute 加字段 —— 各模块手里那份副本是各自维护的）：
    /// · **独立窗口工具**：非抽象 <see cref="EditorWindow"/> 派生类 → 导航里点开一个新窗口（原有行为）；
    /// · **内嵌面板**：<c>static class</c> + <c>public static void DrawTool()</c>（或带
    ///   <c>Action requestRepaint</c> 参数）→ 直接画在 Hub 窗口的内容区里。
    ///   典型用途是"几个开关/状态"这种不值得单开一个窗口的小面板。
    /// </summary>
    public static class CoffeeBeanToolRegistry
    {
        private const string AttributeFullName = "CoffeeBean.EditorTools.CoffeeBeanToolAttribute";

        /// <summary>内嵌面板的绘制方法名（约定）。</summary>
        public const string InlineDrawMethodName = "DrawTool";

        /// <summary>单个工具入口。</summary>
        public sealed class ToolEntry
        {
            public string Title;
            public string Description;
            public string Module;

            /// <summary>独立窗口工具的类型；内嵌面板为 null。</summary>
            public Type WindowType;

            /// <summary>内嵌面板的绘制方法；独立窗口工具为 null。</summary>
            public MethodInfo InlineDraw;

            /// <summary>是否内嵌画在 Hub 内容区（而不是另开窗口）。</summary>
            public bool IsInline => InlineDraw != null;

            /// <summary>打开工具窗口（GetWindow 复用）。内嵌面板没有窗口，调用即无操作。</summary>
            public void Open()
            {
                if (WindowType == null) return;
                var window = EditorWindow.GetWindow(WindowType);
                window.titleContent = new GUIContent(Title);
                window.minSize = new Vector2(480, 360);
            }

            /// <summary>
            /// 内嵌绘制。<paramref name="requestRepaint"/> 交给面板，让它在异步回调（如 UPM 完成）
            /// 之后主动刷新自己 —— 面板是静态类，拿不到宿主窗口。
            /// </summary>
            public void DrawInline(Action requestRepaint)
            {
                if (InlineDraw == null) return;
                object[] args = InlineDraw.GetParameters().Length == 0
                    ? null
                    : new object[] { requestRepaint };
                InlineDraw.Invoke(null, args);
            }
        }

        private static List<ToolEntry> _tools;

        /// <summary>扫描所有已加载程序集，收集工具入口（缓存，Refresh 可重扫）。</summary>
        public static List<ToolEntry> Scan()
        {
            if (_tools != null) return _tools;
            _tools = new List<ToolEntry>();

            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;

                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException)
                {
                    continue;
                }

                foreach (Type type in types)
                {
                    if (type == null || !type.IsClass) continue;

                    // 结构判据放在读 attribute 之前：绝大多数类型连候选都不是，别白读属性
                    bool isWindow = !type.IsAbstract && typeof(EditorWindow).IsAssignableFrom(type);
                    bool isStatic = type.IsAbstract && type.IsSealed; // C# 的 static class
                    if (!isWindow && !isStatic) continue;

                    // 按 attribute 全名匹配（不依赖编译期引用，兼容各模块复制的同名 attribute）
                    Attribute attr = type.GetCustomAttributes(false)
                        .OfType<Attribute>()
                        .FirstOrDefault(a => a.GetType().FullName == AttributeFullName);
                    if (attr == null) continue;

                    var entry = new ToolEntry
                    {
                        Title = ReadStringProperty(attr, "Title") ?? type.Name,
                        Description = ReadStringProperty(attr, "Description"),
                        Module = ReadStringProperty(attr, "Module"),
                    };

                    if (isStatic)
                    {
                        // static 类没有别的用法：写了 DrawTool 才算工具，否则跳过
                        entry.InlineDraw = FindInlineDraw(type);
                        if (entry.InlineDraw == null) continue;
                    }
                    else
                    {
                        entry.WindowType = type;
                    }

                    _tools.Add(entry);
                }
            }

            // 按模块 + 标题排序，稳定显示
            _tools = _tools
                .OrderBy(t => t.Module)
                .ThenBy(t => t.Title)
                .ToList();
            return _tools;
        }

        /// <summary>清空缓存（模块变化后重扫）。</summary>
        public static void RefreshCache() => _tools = null;

        /// <summary>
        /// 找内嵌面板的入口方法：<c>public static void DrawTool()</c> 或
        /// <c>public static void DrawTool(Action requestRepaint)</c>。
        ///
        /// 用**方法签名**约定而不是给 attribute 加字段：attribute 在各模块里是各自维护的副本，
        /// 加字段就得让每个模块跟着改一遍；按签名找则旧的副本一行都不用动。
        /// </summary>
        public static MethodInfo FindInlineDraw(Type type)
        {
            if (type == null) return null;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;

            MethodInfo withRepaint = type.GetMethod(InlineDrawMethodName, flags, null, new[] { typeof(Action) }, null);
            if (withRepaint != null && withRepaint.ReturnType == typeof(void)) return withRepaint;

            MethodInfo plain = type.GetMethod(InlineDrawMethodName, flags, null, Type.EmptyTypes, null);
            return plain != null && plain.ReturnType == typeof(void) ? plain : null;
        }

        private static string ReadStringProperty(Attribute attr, string propertyName)
        {
            PropertyInfo prop = attr.GetType().GetProperty(propertyName);
            return prop?.GetValue(attr, null) as string ?? string.Empty;
        }
    }
}
