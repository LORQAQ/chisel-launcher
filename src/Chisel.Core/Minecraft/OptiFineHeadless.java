// OptiFine 的官方安装器只有 GUI 入口，无人值守安装时需要直接调用它的安装方法。
// 这个类以嵌入资源的形式随 Chisel.Core 一起发布（见 Chisel.Core.csproj），
// 运行时与 OptiFine 安装器 jar 一起拼到 classpath 上。
//
// 必须用 --release 8 编译：安装时使用的 Java 由目标游戏版本决定（可能是 8/17/21），
// 高版本的 class 会在旧运行时上抛 UnsupportedClassVersionError。
//   javac --release 8 -d . OptiFineHeadless.java
public final class OptiFineHeadless {
    public static void main(String[] args) throws Exception {
        if (args.length < 1) {
            throw new IllegalArgumentException("missing game directory");
        }
        optifine.Installer.doInstall(new java.io.File(args[0]));
    }
}
