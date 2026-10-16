using System;
using System.Linq;
using AlvMod.Core.Catalogo;
using AlvMod.Core.Modulacao;
using Xunit;

namespace AlvMod.Core.Tests
{
    public class ModuladorTests
    {
        private static Modulador NovoModulador() => new Modulador(CatalogoPadrao.Familia39Concreto());

        [Fact]
        public void Passo_da_familia_39_e_5cm()
        {
            Assert.Equal(5, NovoModulador().Passo);
        }

        [Fact]
        public void Seiscentos_cm_nao_fecha_e_sugere_599_ou_604()
        {
            var r = NovoModulador().PrimeiraFiada(600);

            Assert.False(r.Sucesso);
            Assert.Equal(new[] { 599, 604 }, r.Sugestoes);
            Assert.Contains("599 ou 604", r.Mensagem);
        }

        [Fact]
        public void Seiscentos_e_quarenta_nao_fecha()
        {
            Assert.False(NovoModulador().PrimeiraFiada(640).Sucesso);
        }

        [Fact]
        public void Quinhentos_e_noventa_e_nove_sao_quinze_blocos_de_39()
        {
            var r = NovoModulador().PrimeiraFiada(599);

            Assert.True(r.Sucesso, r.Mensagem);
            Assert.Equal(15, r.Pecas.Count);
            Assert.All(r.Pecas, p => Assert.Equal("39", p.Peca.Codigo));
            Assert.Equal(599, r.Pecas.Last().Fim);
        }

        [Theory]
        [InlineData(4)]
        [InlineData(9)]
        [InlineData(19)]
        [InlineData(104)]
        [InlineData(604)]
        [InlineData(639)]
        [InlineData(1234)]
        public void Todo_comprimento_terminado_em_4_ou_9_fecha_exatamente(int comprimento)
        {
            var r = NovoModulador().PrimeiraFiada(comprimento);

            Assert.True(r.Sucesso, r.Mensagem);
            Assert.Equal(comprimento, r.Pecas.Last().Fim);
        }

        [Fact]
        public void Tolerancia_aceita_medida_com_ruido_de_desenho()
        {
            Assert.True(NovoModulador().PrimeiraFiada(599.3).Sucesso);
            Assert.False(NovoModulador().PrimeiraFiada(599.8).Sucesso);
        }

        [Fact]
        public void Pecas_principais_ficam_no_inicio_e_especiais_no_fim()
        {
            var r = NovoModulador().PrimeiraFiada(604);

            Assert.True(r.Sucesso, r.Mensagem);
            Assert.Equal("39", r.Pecas.First().Peca.Codigo);
            Assert.NotEqual("39", r.Pecas.Last().Peca.Codigo);
        }

        [Fact]
        public void Peca_desabilitada_nunca_e_usada()
        {
            var familia = CatalogoPadrao.Familia39Concreto();
            familia.Buscar("34").Habilitada = false;
            familia.Buscar("9").Habilitada = false;

            var r = new Modulador(familia).PrimeiraFiada(604);

            Assert.True(r.Sucesso, r.Mensagem);
            Assert.DoesNotContain(r.Pecas, p => p.Peca.Codigo == "34" || p.Peca.Codigo == "9");
        }

        [Fact]
        public void Sem_peca_de_ajuste_comprimento_impossivel_falha_e_nao_forca()
        {
            var familia = CatalogoPadrao.Familia39Concreto();
            foreach (var p in familia.Pecas.Where(p => p.Codigo != "39")) p.Habilitada = false;

            var r = new Modulador(familia).PrimeiraFiada(604);

            Assert.False(r.Sucesso);
            Assert.Empty(r.Pecas);
        }

        [Theory]
        [InlineData(599)]
        [InlineData(604)]
        [InlineData(394)]
        [InlineData(1009)]
        public void Segunda_fiada_nao_tem_junta_a_prumo(int comprimento)
        {
            var modulador = NovoModulador();
            var primeira = modulador.PrimeiraFiada(comprimento);
            var juntas1 = primeira.Juntas(1).ToList();

            var segunda = modulador.SegundaFiada(comprimento, juntas1);

            Assert.True(segunda.Sucesso, segunda.Mensagem);
            Assert.Equal(comprimento, segunda.Pecas.Last().Fim);
            foreach (var j2 in segunda.Juntas(1))
                Assert.All(juntas1, j1 => Assert.True(Math.Abs(j1 - j2) >= 5, $"junta a prumo em {j2}"));
        }

        [Fact]
        public void Segunda_fiada_de_599_comeca_com_meio_bloco()
        {
            var modulador = NovoModulador();
            var juntas1 = modulador.PrimeiraFiada(599).Juntas(1);

            var segunda = modulador.SegundaFiada(599, juntas1);

            Assert.Equal("19", segunda.Pecas.First().Peca.Codigo);
            Assert.Equal("19", segunda.Pecas.Last().Peca.Codigo);
        }

        [Fact]
        public void Segunda_fiada_impossivel_reporta_em_vez_de_desenhar_prumo()
        {
            // Juntas da 1ª fiada a cada 5 cm: qualquer 2ª fiada teria junta a prumo.
            var juntas = Enumerable.Range(1, 100).Select(i => i * 5 - 0.5);

            var r = NovoModulador().SegundaFiada(599, juntas);

            Assert.False(r.Sucesso);
            Assert.Contains("prumo", r.Mensagem);
        }
    }
}
