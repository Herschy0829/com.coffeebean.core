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
    /// 标记的 EditorWindow 类型，供 CoffeeBean Hub 窗口列出入口。
    /// </summary>
    public static class CoffeeBeanToolRegistry
    {
        private const string AttributeFullName = "CoffeeBean.EditorTools.CoffeeBeanToolAttribute";

        /// <summary>单个工具入口。</summary>
        public sealed class ToolEntry
        {
            public string Title;
            public string Description;
            public string Module;
            public Type WindowType;

            /// <summary>打开工具窗口（GetWindow 复用）。</summary>
            public void Open()
            {
                if (WindowType == null) return;
                var window = EditorWindow.GetWindow(WindowType);
                window.titleContent = new GUIContent(Title);
                window.minSize = new Vector2(480, 360);
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
                    if (type == null || !type.IsClass || type.IsAbstract) continue;
                    if (!typeof(EditorWindow).IsAssignableFrom(type)) continue;

                    // 按 attribute 全名匹配（不依赖编译期引用，兼容各模块复制的同名 attribute）
                    Attribute attr = type.GetCustomAttributes(false)
                        .OfType<Attribute>()
                        .FirstOrDefault(a => a.GetType().FullName == AttributeFullName);
                    if (attr == null) continue;

                    var entry = new ToolEntry
                    {
                        WindowType = type,
                        Title = ReadStringProperty(attr, "Title") ?? type.Name,
                        Description = ReadStringProperty(attr, "Description"),
                        Module = ReadStringProperty(attr, "Module"),
                    };
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

        private static string ReadStringProperty(Attribute attr, string propertyName)
        {
            PropertyInfo prop = attr.GetType().GetProperty(propertyName);
            return prop?.GetValue(attr, null) as string ?? string.Empty;
        }
    }
}
