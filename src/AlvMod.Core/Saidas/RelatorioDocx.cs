using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace AlvMod.Core.Saidas
{
    public enum Gravidade
    {
        Aviso,
        Inconsistencia
    }

    public sealed class Ocorrencia
    {
        public DateTime Quando { get; set; } = DateTime.Now;
        public Gravidade Gravidade { get; set; }
        public string Comando { get; set; }

        /// <summary>Onde está no desenho (ex.: "Trecho 2, de (120.0, 340.5) a (720.0, 340.5)").</summary>
        public string Local { get; set; }

        public string Descricao { get; set; }
    }

    /// <summary>
    /// Relatório de inconsistências em .docx (questionário, item 50), gerado sem bibliotecas externas:
    /// um .docx é um zip com três arquivos XML.
    /// </summary>
    public static class RelatorioDocx
    {
        public static byte[] Gerar(string titulo, string subtitulo, IReadOnlyList<Ocorrencia> ocorrencias)
        {
            var corpo = new StringBuilder();
            corpo.Append(Paragrafo(titulo, negrito: true, tamanho: 32));
            corpo.Append(Paragrafo(subtitulo, tamanho: 20));
            corpo.Append(Paragrafo(""));

            if (ocorrencias.Count == 0)
            {
                corpo.Append(Paragrafo("Nenhuma inconsistência registrada nesta sessão."));
            }
            else
            {
                corpo.Append(Paragrafo($"{ocorrencias.Count} ocorrência(s) para conferir e editar:", negrito: true));
                int i = 0;
                foreach (var o in ocorrencias)
                {
                    i++;
                    string tipo = o.Gravidade == Gravidade.Inconsistencia ? "INCONSISTÊNCIA" : "AVISO";
                    corpo.Append(Paragrafo($"{i}. [{tipo}] {o.Comando} — {o.Quando:dd/MM/yyyy HH:mm}", negrito: true));
                    if (!string.IsNullOrEmpty(o.Local)) corpo.Append(Paragrafo("Local: " + o.Local));
                    corpo.Append(Paragrafo(o.Descricao));
                    corpo.Append(Paragrafo(""));
                }
            }

            using (var ms = new MemoryStream())
            {
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    Escrever(zip, "[Content_Types].xml",
                        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                        "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                        "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
                        "</Types>");
                    Escrever(zip, "_rels/.rels",
                        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
                        "</Relationships>");
                    Escrever(zip, "word/document.xml",
                        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                        "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>" +
                        corpo +
                        "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1134\" w:right=\"1134\" w:bottom=\"1134\" w:left=\"1134\" w:header=\"0\" w:footer=\"0\" w:gutter=\"0\"/></w:sectPr>" +
                        "</w:body></w:document>");
                }
                return ms.ToArray();
            }
        }

        private static string Paragrafo(string texto, bool negrito = false, int tamanho = 22)
        {
            string props = (negrito ? "<w:b/>" : "") + $"<w:sz w:val=\"{tamanho}\"/>";
            return $"<w:p><w:r><w:rPr>{props}</w:rPr><w:t xml:space=\"preserve\">{SecurityElement.Escape(texto ?? "")}</w:t></w:r></w:p>";
        }

        private static void Escrever(ZipArchive zip, string nome, string conteudo)
        {
            var entrada = zip.CreateEntry(nome, CompressionLevel.Optimal);
            using (var s = entrada.Open())
            {
                var bytes = new UTF8Encoding(false).GetBytes(conteudo);
                s.Write(bytes, 0, bytes.Length);
            }
        }
    }
}
