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
            Assert.Equal("P-Bloco4", p.NomeBlocoDwg);
            Assert.Equal("P-Bloco 14x4", p.Layer);
            Assert.Equal(8, p.Cor);
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
                Familia = CatalogoPadrao.Concreto39,
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
            var blocos = Enumerable.Repeat("P-Bloco39", 15).Concat(new[] { "P-Bloco4", "p-bloco4", "OUTRO" });

            var linhas = Quantitativo.Contar(blocos, cat, new Dictionary<string, double> { ["39"] = 4.0 });
            var csv = Encoding.UTF8.GetString(Quantitativo.ParaCsv(linhas, "Obra teste"));

            Assert.Equal(15, linhas.Single(l => l.Bloco == "P-Bloco39").Quantidade);
            Assert.Equal(60.0, linhas.Single(l => l.Bloco == "P-Bloco39").Total);
            Assert.Null(linhas.Single(l => l.Bloco == "OUTRO").Peca);
            Assert.Contains("P-Bloco39;CONCRETO-39;39;39x14x19;Principal;15;4,00;60,00", csv);
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
    
        [Fact]
        public void Catalogo_da_v02_e_migrado_para_os_nomes_reais_sem_perder_edicoes()
        {
            var antigo = "{\"familias\":[" +
                "{\"nome\":\"CONCRETO-39\",\"pecas\":[" +
                "{\"codigo\":\"39\",\"comprimento\":39,\"funcao\":\"Principal\",\"blocoPlanta\":\"P-39X14X19\",\"layer\":\"BLOCO 14X39\",\"custo\":10}," +
                "{\"codigo\":\"34\",\"comprimento\":34,\"funcao\":\"Amarracao\",\"blocoPlanta\":\"MEU-34\",\"layer\":\"MEU LAYER\",\"custo\":20}," +
                "{\"codigo\":\"C39\",\"comprimento\":39,\"funcao\":\"Canaleta\",\"blocoPlanta\":\"P-CAN-39X14X19\",\"custo\":0}]}," +
                "{\"nome\":\"CERAMICO-39\",\"pecas\":[{\"codigo\":\"39\",\"comprimento\":39,\"blocoPlanta\":\"P-CER-39X14X19\",\"custo\":10}]}]}";
            var caminho = Path.Combine(Path.GetTempPath(), "alvmod-mig-" + System.Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(caminho, antigo);

            var cat = CatalogoBlocos.Carregar(caminho);
            var f = cat.Buscar(CatalogoPadrao.Concreto39);

            Assert.Equal("P-Bloco39", f.Buscar("39").NomeBlocoDwg);
            Assert.Equal("P-Bloco 14x39", f.Buscar("39").Layer);
            Assert.Equal(74, f.Buscar("39").Cor);
            Assert.Equal("MEU-34", f.Buscar("34").NomeBlocoDwg);      // editado pelo usuário: não mexe
            Assert.Null(f.Buscar("C39"));                              // canaleta suposta: sai
            Assert.Null(cat.Buscar("CERAMICO-39"));                    // família cerâmica padrão: sai
            Assert.Equal(CatalogoPadrao.VersaoAtual, CatalogoBlocos.Carregar(caminho).VersaoPadrao); // regravado
        }

        [Fact]
        public void Folga_de_marco_porta_11_janela_6_dividida_entre_os_lados()
        {
            var cfg = new ConfigObra();

            Assert.Equal(5.5, cfg.RecuoPorLado(TipoAbertura.Porta));
            Assert.Equal(3, cfg.RecuoPorLado(TipoAbertura.Janela));
            Assert.Equal(0, cfg.RecuoPorLado(TipoAbertura.Passagem));
            Assert.Equal(0, cfg.RecuoPorLado(TipoAbertura.Parede));
        }
    
        [Fact]
        public void Custos_da_v3_migram_para_a_nova_prioridade_e_edicoes_do_usuario_ficam()
        {
            var antigo = "{\"versaoPadrao\":3,\"familias\":[{\"nome\":\"CONCRETO-39\",\"pecas\":[" +
                "{\"codigo\":\"39\",\"comprimento\":39,\"funcao\":\"Principal\",\"blocoPlanta\":\"P-Bloco39\",\"custo\":10}," +
                "{\"codigo\":\"19\",\"comprimento\":19,\"funcao\":\"Meio\",\"blocoPlanta\":\"P-Bloco19\",\"custo\":12}," +
                "{\"codigo\":\"54\",\"comprimento\":54,\"funcao\":\"Encontro\",\"blocoPlanta\":\"P-Bloco54\",\"custo\":14}," +
                "{\"codigo\":\"34\",\"comprimento\":34,\"funcao\":\"Amarracao\",\"blocoPlanta\":\"P-Bloco34\",\"custo\":25}," +
                "{\"codigo\":\"9\",\"comprimento\":9,\"funcao\":\"Compensador\",\"blocoPlanta\":\"P-Bloco9\",\"custo\":22}," +
                "{\"codigo\":\"4\",\"comprimento\":4,\"funcao\":\"Compensador\",\"blocoPlanta\":\"P-Bloco4\",\"custo\":24}]}]}";
            var caminho = Path.Combine(Path.GetTempPath(), "alvmod-mig4-" + System.Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(caminho, antigo);

            var f = CatalogoBlocos.Carregar(caminho).Buscar(CatalogoPadrao.Concreto39);

            Assert.Equal(10, f.Buscar("39").Custo);
            Assert.Equal(60, f.Buscar("19").Custo);
            Assert.Equal(10, f.Buscar("54").Custo);
            Assert.Equal(25, f.Buscar("34").Custo);   // editado pelo usuário: não mexe
            Assert.Equal(80, f.Buscar("9").Custo);
            Assert.Equal(90, f.Buscar("4").Custo);
            Assert.Equal(CatalogoPadrao.VersaoAtual, CatalogoBlocos.Carregar(caminho).VersaoPadrao);
        }

        [Fact]
        public void Ordem_de_prioridade_do_catalogo_padrao_e_39_34_19_e_bolachas_por_ultimo()
        {
            var f = CatalogoPadrao.Familia39Concreto();
            int Custo(string c) => f.Buscar(c).Custo;

            Assert.True(Custo("39") < Custo("34"));
            Assert.True(Custo("34") < Custo("19"));
            Assert.True(Custo("19") < Custo("9"));
            Assert.True(Custo("9") < Custo("4"));
        }
    }
}
