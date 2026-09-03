using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using CoffeeBean.EditorTools;

namespace CoffeeBean.Tests
{
    /// <summary>
    /// 构建模式（Beta/Release）符号切换测试。
    /// 操作当前激活平台组的 PlayerSettings symbols；SetUp 先清场为 Release 基线（不依赖初始状态），
    /// TearDown 还原，避免污染其它测试。
    /// </summary>
    public class CBuildModeEditorTests
    {
        NamedBuildTarget _target;
        string _original;

        [SetUp]
        public void SetUp()
        {
            _target = NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
            _original = PlayerSettings.GetScriptingDefineSymbols(_target);
            // 清场：确保从 Release 基线开始
            CBuildModeEditor.ApplyMode(_target, CBuildModeEditor.Mode.Release);
        }

        [TearDown]
        public void TearDown()
        {
            PlayerSettings.SetScriptingDefineSymbols(_target, _original);
        }

        [Test]
        public void Beta_AddsModeDefines_Release_Removes_AndPreservesOthers()
        {
            // Beta：两个模式宏都应加入，既有非模式符号保留
            CBuildModeEditor.ApplyMode(_target, CBuildModeEditor.Mode.Beta);
            string beta = PlayerSettings.GetScriptingDefineSymbols(_target);
            StringAssert.Contains(CBuildModeEditor.DevToolsDefine, beta);
            StringAssert.Contains(CBuildModeEditor.LogDefine, beta);
            AssertNonModeSymbolsPreserved(beta);

            // Release：模式宏移除，既有非模式符号仍保留
            CBuildModeEditor.ApplyMode(_target, CBuildModeEditor.Mode.Release);
            string release = PlayerSettings.GetScriptingDefineSymbols(_target);
            StringAssert.DoesNotContain(CBuildModeEditor.DevToolsDefine, release);
            StringAssert.DoesNotContain(CBuildModeEditor.LogDefine, release);
            AssertNonModeSymbolsPreserved(release);
        }

        /// <summary>断言原始符号里除模式宏外的部分仍存在（模式宏随模式切换增减）。</summary>
        void AssertNonModeSymbolsPreserved(string symbols)
        {
            if (string.IsNullOrEmpty(_original)) return;
            foreach (var s in _original.Split(';'))
            {
                if (string.IsNullOrEmpty(s)) continue;
                if (s == CBuildModeEditor.DevToolsDefine || s == CBuildModeEditor.LogDefine) continue;
                StringAssert.Contains(s, symbols);
            }
        }

        [Test]
        public void CurrentMode_ReflectsSymbols()
        {
            CBuildModeEditor.ApplyMode(_target, CBuildModeEditor.Mode.Beta);
            Assert.AreEqual(CBuildModeEditor.Mode.Beta, CBuildModeEditor.CurrentMode(_target));

            CBuildModeEditor.ApplyMode(_target, CBuildModeEditor.Mode.Release);
            Assert.AreEqual(CBuildModeEditor.Mode.Release, CBuildModeEditor.CurrentMode(_target));
        }
    }
}
