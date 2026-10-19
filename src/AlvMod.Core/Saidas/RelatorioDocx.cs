using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
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
        /// <param name="memoria">Memória de cálculo (um trecho por linha); nula ou vazia = sem a seção.</param>
        public static byte[] Gerar(string titulo, string subtitulo, IReadOnlyList<Ocorrencia> ocorrencias,
            IReadOnlyList<LinhaMemoria> memoria = null)
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

            if (memoria != null && memoria.Count > 0)
            {
                corpo.Append(Paragrafo(""));
                corpo.Append(Paragrafo("Memória de cálculo", negrito: true, tamanho: 28));
                corpo.Append(Paragrafo("Cada trecho de alvenaria modulado: comprimento entre as pontas, o que há em cada ponta " +
                                       "(início | fim), peças do início ao fim e resultado.", tamanho: 18));
                var linhas = new List<string[]>
                {
                    new[] { "Comando", "Parede", "Fiada", "Compr. (cm)", "Pontas", "Peças", "Situação", "Observações" }
                };
                linhas.AddRange(memoria.Select(m => new[]
                {
                    m.Comando, m.Parede, m.Fiada.ToString(), m.Comprimento.ToString("0.#", CultureInfo.GetCultureInfo("pt-BR")),
                    m.Pontas, m.Pecas, m.Situacao, m.Observacoes
                }));
                corpo.Append(Tabela(linhas));
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

        private static string Tabela(List<string[]> linhas)
        {
            var t = new StringBuilder("<w:tbl><w:tblPr><w:tblStyle w:val=\"TableGrid\"/><w:tblW w:w=\"5000\" w:type=\"pct\"/>" +
                "<w:tblBorders><w:top w:val=\"single\" w:sz=\"4\"/><w:left w:val=\"single\" w:sz=\"4\"/>" +
                "<w:bottom w:val=\"single\" w:sz=\"4\"/><w:right w:val=\"single\" w:sz=\"4\"/>" +
                "<w:insideH w:val=\"single\" w:sz=\"4\"/><w:insideV w:val=\"single\" w:sz=\"4\"/></w:tblBorders></w:tblPr>");
            for (int i = 0; i < linhas.Count; i++)
            {
                t.Append(i == 0 ? "<w:tr><w:trPr><w:tblHeader/></w:trPr>" : "<w:tr>");
                foreach (var celula in linhas[i])
                    t.Append("<w:tc>").Append(Paragrafo(celula, negrito: i == 0, tamanho: 16)).Append("</w:tc>");
                t.Append("</w:tr>");
            }
            return t.Append("</w:tbl>").ToString();
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
