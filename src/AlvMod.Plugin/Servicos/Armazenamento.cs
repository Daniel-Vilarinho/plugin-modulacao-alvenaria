using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AlvMod.Core.Catalogo;
using AlvMod.Core.Saidas;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace AlvMod.Plugin.Servicos
{
    /// <summary>Catálogo de famílias/peças deste computador (%APPDATA%\AlvMod\catalogo.json).</summary>
    internal static class CatalogoLocal
    {
        public static string Caminho =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AlvMod", "catalogo.json");

        public static CatalogoBlocos Carregar() => CatalogoBlocos.Carregar(Caminho);
    }

    /// <summary>Configuração da obra gravada no próprio DWG (dicionário de objetos nomeados).</summary>
    internal static class ObraNoDesenho
    {
        private const string Chave = "ALVMOD_OBRA";
        private const int TamanhoPedaco = 250;

        public static ConfigObra Ler(Database db, Transaction tr)
        {
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!nod.Contains(Chave)) return new ConfigObra();

            var xr = (Xrecord)tr.GetObject(nod.GetAt(Chave), OpenMode.ForRead);
            var json = string.Concat((xr.Data?.AsArray() ?? new TypedValue[0]).Select(tv => tv.Value as string));
            try { return AlvMod.Core.Catalogo.Json.LerTexto<ConfigObra>(json); }
            catch { return new ConfigObra(); }
        }

        public static void Gravar(Database db, Transaction tr, ConfigObra cfg)
        {
            var json = AlvMod.Core.Catalogo.Json.EscreverTexto(cfg);
            var rb = new ResultBuffer();
            for (int i = 0; i < json.Length; i += TamanhoPedaco)
                rb.Add(new TypedValue((int)DxfCode.Text, json.Substring(i, Math.Min(TamanhoPedaco, json.Length - i))));

            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
            if (nod.Contains(Chave))
            {
                var xr = (Xrecord)tr.GetObject(nod.GetAt(Chave), OpenMode.ForWrite);
                xr.Data = rb;
            }
            else
            {
                var xr = new Xrecord { Data = rb };
                nod.SetAt(Chave, xr);
                tr.AddNewlyCreatedDBObject(xr, true);
            }
        }
    }

    /// <summary>
    /// Inconsistências, avisos e memória de cálculo de cada desenho aberto, para o relatório (ALVRELATORIO).
    /// Ficam na memória enquanto o desenho está aberto.
    /// </summary>
    internal static class Registro
    {
        private sealed class DoDesenho
        {
            public readonly List<Ocorrencia> Ocorrencias = new List<Ocorrencia>();
            public readonly List<LinhaMemoria> Memoria = new List<LinhaMemoria>();
        }

        private static readonly Dictionary<Document, DoDesenho> PorDesenho = new Dictionary<Document, DoDesenho>();

        private static DoDesenho De(Document doc)
        {
            if (!PorDesenho.TryGetValue(doc, out var d))
            {
                d = new DoDesenho();
                PorDesenho[doc] = d;
                doc.BeginDocumentClose += (s, e) => PorDesenho.Remove(doc);
            }
            return d;
        }

        public static List<Ocorrencia> Do(Document doc) => De(doc).Ocorrencias;

        public static List<LinhaMemoria> MemoriaDo(Document doc) => De(doc).Memoria;

        /// <summary>
        /// Começo de um comando de pavimento inteiro (ALVPLANTA, ALVPLANTA2): o resultado anterior dele é
        /// substituído, para o relatório não repetir os trechos quando o comando é rodado de novo.
        /// </summary>
        public static void Reiniciar(Document doc, string comando)
        {
            var d = De(doc);
            d.Ocorrencias.RemoveAll(o => o.Comando == comando);
            d.Memoria.RemoveAll(m => m.Comando == comando);
        }

        public static void Adicionar(Document doc, Gravidade gravidade, string comando, string local, string descricao) =>
            De(doc).Ocorrencias.Add(new Ocorrencia { Gravidade = gravidade, Comando = comando, Local = local, Descricao = descricao });

        public static void Adicionar(Document doc, LinhaMemoria linha) => De(doc).Memoria.Add(linha);
    }

    internal static class Arquivos
    {
        /// <summary>Caminho de saída ao lado do DWG (ou em Documentos, se o desenho ainda não foi salvo).</summary>
        public static string AoLadoDoDesenho(Document doc, string sufixo, string extensao)
        {
            string dwg = doc.Database.Filename;
            bool salvo = !string.IsNullOrEmpty(dwg) && File.Exists(dwg) &&
                         !dwg.EndsWith(".dwt", StringComparison.OrdinalIgnoreCase) &&
                         !Path.GetDirectoryName(dwg).StartsWith(Path.GetTempPath().TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            string pasta = salvo ? Path.GetDirectoryName(dwg) : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string nome = Path.GetFileNameWithoutExtension(doc.Name);
            return Path.Combine(pasta, $"{nome}-{sufixo}-{DateTime.Now:yyyyMMdd-HHmm}{extensao}");
        }

        public static void Abrir(string caminho)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(caminho) { UseShellExecute = true }); }
            catch { /* sem programa associado: o caminho já foi mostrado na linha de comando */ }
        }

        public static void Salvar(string caminho, byte[] conteudo) => File.WriteAllBytes(caminho, conteudo);
    }
}
