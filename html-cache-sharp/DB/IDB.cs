namespace HtmlCache.DB
{
    internal interface IDB
    {
        public RenderModel? FindByHash(string hash);
        public bool Save(RenderModel model);
    }
}
