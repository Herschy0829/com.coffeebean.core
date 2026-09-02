using NUnit.Framework;

namespace CoffeeBean.Tests
{
    /// <summary>Core 集成宏安装器测试：COFFEEBEAN_CORE 宏应被加入全局 define。</summary>
    public class CoffeeBeanDefineInstallerTests
    {
        [Test]
        public void EnsureDefine_AddsCoreDefine()
        {
            // 运行安装（幂等）
            CoffeeBean.EditorTools.CoffeeBeanDefineInstaller.EnsureDefine();

            Assert.IsTrue(
                CoffeeBean.EditorTools.CoffeeBeanDefineInstaller.IsDefinePresent(),
                "COFFEEBEAN_CORE 宏应存在于全局脚本定义中（模块 Bridge 集成的前提）");
        }

        [Test]
        public void EnsureDefine_IsIdempotent()
        {
            CoffeeBean.EditorTools.CoffeeBeanDefineInstaller.EnsureDefine();
            CoffeeBean.EditorTools.CoffeeBeanDefineInstaller.EnsureDefine();

            Assert.IsTrue(
                CoffeeBean.EditorTools.CoffeeBeanDefineInstaller.IsDefinePresent());
        }
    }
}
