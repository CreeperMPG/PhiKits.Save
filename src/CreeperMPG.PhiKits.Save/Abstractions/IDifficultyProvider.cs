namespace CreeperMPG.PhiKits.Save.Abstractions
{
    /// <summary>
    /// 曲目定数（难度常量）的提供者。
    /// <para>
    /// 本库不内置任何定数数据，RKS 计算需要由消费方实现本接口来提供。
    /// 形状与 <c>PhigrosArchive.Abstractions.IDifficultyProvider</c> 保持一致，
    /// 因此同一份实现可以同时供两代库使用。
    /// </para>
    /// </summary>
    public interface IDifficultyProvider
    {
        /// <summary>定数数据是否已加载</summary>
        bool IsLoaded { get; }

        /// <summary>
        /// 查询某曲目某难度的定数；查不到时返回 null。
        /// </summary>
        /// <param name="songId">曲目 ID，即 <c>Records</c> 字典的键</param>
        /// <param name="difficultyIndex">难度序号：0=EZ 1=HD 2=IN 3=AT 4=Legacy</param>
        float? GetDifficulty(string songId, int difficultyIndex);
    }
}
