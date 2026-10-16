using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using AlvMod.Core.Catalogo;
using AlvMod.Core.Saidas;
using Xunit;

namespace AlvMod.Core.Tests
{
    public class CatalogoESaidasTests
    {
        [Fact]
        public void Catalogo_e_criado_na_primeira_vez_e_relido_igual()
        {
            var caminho = Path.Combine(Path.GetTempPath(), "alvmod-teste-" + System.Guid.NewGuid().ToString("N"), "catalogo.json");

            var criado = CatalogoBlocos.Carregar(caminho);
            var relido = CatalogoBlocos.Carregar(caminho);

            Assert.True(File.Exists(caminho));
            Assert.Equal(criado.Familias.Count, relido.Familias.Count);
            var p = relido.Buscar(CatalogoPadrao.Concreto39).Buscar("4");
            Assert.Equal(FuncaoPeca.Compensador, p.Funcao);
            Assert.False(p.Grauteavel);
            Assert.Equal("P-4X14X19", p.NomeBlocoDwg);
            Assert.Contains("\"funcao\": \"Compensador\"", File.ReadAllText(caminho));
        }

        [Fact]
        public void Json_editado_a_mao_sem_campos_opcionais_usa_os_padroes()
        {
            var json = "{\"familias\":[{\"nome\":\"TESTE\",\"pecas\":[{\"codigo\":\"29\",\"comprimento\":29,\"funcao\":\"principal\",\"custo\":10}]}]}";

            var cat = Json.LerTexto<CatalogoBlocos>(json);
            var familia = cat.Buscar("teste");
            var peca = familia.Buscar("29");

            Assert.Equal(1, familia.Junta);
            Assert.Equal(14, peca.Largura);
            Assert.True(peca.Habilitada);
            Assert.True(peca.Grauteavel);
            Assert.Equal(FuncaoPeca.Principal, peca.Funcao);
        }

        [Fact]
        public void Config_da_obra_ida_e_volta()
        {
            var cfg = new ConfigObra
            {
                Familia = CatalogoPadrao.Ceramico39,
                PecasDesabilitadas = new List<string> { "4" },
                FatorCm = 100,
                Escala = 25,
                Deslocamento2F = new[] { 5000.0, 0, 0 },
                Precos = new Dictionary<string, double> { ["39"] = 3.5 }
            };

            var volta = Json.LerTexto<ConfigObra>(Json.EscreverTexto(cfg));

            Assert.Equal(cfg.Familia, volta.Familia);
            Assert.Equal(new[] { "4" }, volta.PecasDesabilitadas);
            Assert.Equal(100, volta.FatorCm);
            Assert.Equal(25, volta.Escala);
            Assert.Equal(5000, volta.Deslocamento2F[0]);
            Assert.Equal(3.5, volta.Precos["39"]);
        }

        [Fact]
        public void Quantitativo_conta_por_bloco_e_calcula_total_com_preco()
        {
            var cat = CatalogoPadrao.Criar();
            var blocos = Enumerable.Repeat("P-39X14X19", 15).Concat(new[] { "P-4X14X19", "P-4X14X19", "OUTRO" });

            var linhas = Quantitativo.Contar(blocos, cat, new Dictionary<string, double> { ["39"] = 4.0 });
            var csv = Encoding.UTF8.GetString(Quantitativo.ParaCsv(linhas, "Obra teste"));

            Assert.Equal(15, linhas.Single(l => l.Bloco == "P-39X14X19").Quantidade);
            Assert.Equal(60.0, linhas.Single(l => l.Bloco == "P-39X14X19").Total);
            Assert.Null(linhas.Single(l => l.Bloco == "OUTRO").Peca);
            Assert.Contains("P-39X14X19;CONCRETO-39;39;39x14x19;Principal;15;4,00;60,00", csv);
            Assert.Contains("TOTAL;;;;;18;;60,00", csv);
        }

        [Fact]
        public void Relatorio_docx_e_um_zip_com_document_xml_e_texto_escapado()
        {
            var bytes = RelatorioDocx.Gerar("Relatório", "obra.dwg", new[]
            {
                new Ocorrencia { Gravidade = Gravidade.Inconsistencia, Comando = "ALV1FIADA", Local = "Trecho 1", Descricao = "600 < 604 & \"x\"" }
            });

            using (var zip = new ZipArchive(new MemoryStream(bytes)))
            {
                Assert.NotNull(zip.GetEntry("[Content_Types].xml"));
                Assert.NotNull(zip.GetEntry("_rels/.rels"));
                using (var r = new StreamReader(zip.GetEntry("word/document.xml").Open()))
                {
                    var xml = r.ReadToEnd();
                    Assert.Contains("600 &lt; 604 &amp; &quot;x&quot;", xml);
                    System.Xml.Linq.XDocument.Parse(xml); // XML bem formado
                }
            }
        }
    }
}
