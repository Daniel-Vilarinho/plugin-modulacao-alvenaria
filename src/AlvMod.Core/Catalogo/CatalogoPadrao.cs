using System;
using System.Collections.Generic;
using System.Linq;

namespace AlvMod.Core.Catalogo
{
    /// <summary>
    /// Catálogo inicial, gravado no arquivo de catálogo na primeira execução. Depois disso o usuário
    /// edita pelo comando ALVCATALOGO (ou direto no JSON).
    /// Nomes de bloco, layers e cores iguais aos da modulação à mão do engenheiro: P-Bloco39, P-Bloco 34,
    /// P-Bloco 19, P-Bloco 54 nos layers "P-Bloco 14x.."; bolacha e Bloco 9x14 no layer "BOLACHA 14X4".
    /// Só blocos de concreto.
    /// </summary>
    public static class CatalogoPadrao
    {
        public const string Concreto39 = "CONCRETO-39";

        /// <summary>Sobe quando os padrões mudam, para migrar catálogos já gravados (ver <see cref="Migrar"/>).</summary>
        public const int VersaoAtual = 6;

        public static CatalogoBlocos Criar() => new CatalogoBlocos
        {
            VersaoPadrao = VersaoAtual,
            Familias = new List<Familia> { Familia39Concreto() }
        };

        public static Familia Familia39Concreto() => new Familia
        {
            Nome = Concreto39,
            Material = Material.Concreto,
            Junta = 1,
            Pecas = new List<Peca>
            {
                // Custos = ordem de prioridade do motor, calibrada na modulação do engenheiro: 39 primeiro, 34 em
                // segundo (ele usa até quatro 34 seguidos junto ao encontro antes de recorrer a bolacha), 19 só na
                // ombreira de porta, bolachas por último (só onde nenhum 34 resolve). O 54 é peça fixa (T e cruz).
                Nova(39, FuncaoPeca.Principal, 10, cor: 74, bloco: "P-Bloco39"),
                Nova(19, FuncaoPeca.Meio, 60, cor: 204, bloco: "P-Bloco 19"),
                Nova(54, FuncaoPeca.Encontro, 10, cor: 30, bloco: "P-Bloco 54"),
                Nova(34, FuncaoPeca.Amarracao, 45, cor: 170, bloco: "P-Bloco 34"),
                Nova(9, FuncaoPeca.Compensador, 150, cor: 8, grauteavel: false, bloco: "Bloco 9x14", layer: "BOLACHA 14X4"),
                Nova(4, FuncaoPeca.Compensador, 160, cor: 8, grauteavel: false, bloco: "bolacha", layer: "BOLACHA 14X4"),
            }
        };

        /// <summary>
        /// Custo sugerido ao cadastrar uma peça nova (ALVCATALOGO), na mesma escala do catálogo padrão:
        /// principal 10, encontro 10, 34 de amarração 45, meio bloco 60, compensadores 150 (≥ 9 cm) ou 160.
        /// </summary>
        public static int CustoSugerido(FuncaoPeca funcao, int comprimento)
        {
            switch (funcao)
            {
                case FuncaoPeca.Principal: return 10;
                case FuncaoPeca.Encontro: return 10;
                case FuncaoPeca.Amarracao: return 45;
                case FuncaoPeca.Meio: return 60;
                case FuncaoPeca.Compensador: return comprimento >= 9 ? 150 : 160;
                default: return 0;
            }
        }

        private static Peca Nova(int comprimento, FuncaoPeca funcao, int custo, short cor,
            bool grauteavel = true, bool habilitada = true, string bloco = null, string layer = null) => new Peca
        {
            Codigo = comprimento.ToString(),
            Comprimento = comprimento,
            Largura = 14,
            Altura = 19,
            Funcao = funcao,
            NomeBlocoDwg = bloco ?? $"P-Bloco{comprimento}",
            Layer = layer ?? $"P-Bloco 14x{comprimento}",
            Cor = cor,
            Custo = custo,
            Grauteavel = grauteavel,
            Habilitada = habilitada
        };

        /// <summary>
        /// Atualiza um catálogo gravado por uma versão anterior do plugin: as peças que ainda estão com os
        /// nomes padrão antigos passam para os nomes, layers e cores reais; a família cerâmica padrão
        /// (que não é usada) sai se não foi alterada. Peças editadas pelo usuário não são tocadas.
        /// </summary>
        /// <returns>true se algo mudou e o arquivo deve ser regravado.</returns>
        public static bool Migrar(CatalogoBlocos catalogo)
        {
            if (catalogo.VersaoPadrao >= VersaoAtual) return false;
            if (catalogo.VersaoPadrao < 2) MigrarNomes(catalogo);

            // v3: o 54 deixa de ser desligado; o motor passa a usá-lo só nas pontas em encontro.
            foreach (var p in catalogo.Familias.SelectMany(f => f.Pecas))
                if (p.Funcao == FuncaoPeca.Encontro && p.NomeBlocoDwg == "P-Bloco54") p.Habilitada = true;

            // v4/v5: nova escala de prioridades. Só troca o custo de quem ainda está com um valor padrão antigo.
            var antigos = new Dictionary<string, (int[] antigos, int novo)>
            {
                ["39"] = (new[] { 10 }, 10), ["19"] = (new[] { 12 }, 60), ["54"] = (new[] { 14 }, 10),
                ["34"] = (new[] { 20, 40 }, 45), ["9"] = (new[] { 22 }, 80), ["4"] = (new[] { 24 }, 90)
            };
            foreach (var f in catalogo.Familias)
                foreach (var p in f.Pecas)
                    if (p.Codigo != null && antigos.TryGetValue(p.Codigo, out var c) && c.antigos.Contains(p.Custo))
                        p.Custo = c.novo;

            // v6: nomes, layers e custos da modulação real do engenheiro. Só muda o que ainda está no padrão anterior.
            var v6 = Familia39Concreto();
            var anteriores = new Dictionary<string, (string bloco, string layer, int custo)>
            {
                ["39"] = ("P-Bloco39", "P-Bloco 14x39", 10), ["34"] = ("P-Bloco34", "P-Bloco 14x34", 45),
                ["19"] = ("P-Bloco19", "P-Bloco 14x19", 60), ["54"] = ("P-Bloco54", "P-Bloco 14x54", 10),
                ["9"] = ("P-Bloco9", "P-Bloco 14x9", 80), ["4"] = ("P-Bloco4", "P-Bloco 14x4", 90)
            };
            foreach (var f in catalogo.Familias)
                foreach (var p in f.Pecas)
                {
                    if (p.Codigo == null || !anteriores.TryGetValue(p.Codigo, out var a)) continue;
                    var novo = v6.Buscar(p.Codigo);
                    if (p.NomeBlocoDwg == a.bloco) p.NomeBlocoDwg = novo.NomeBlocoDwg;
                    if (p.Layer == a.layer) p.Layer = novo.Layer;
                    if (p.Custo == a.custo) p.Custo = novo.Custo;
                }

            catalogo.VersaoPadrao = VersaoAtual;
            return true;
        }

        private static void MigrarNomes(CatalogoBlocos catalogo)
        {
            var novo = Familia39Concreto();
            foreach (var f in catalogo.Familias)
            {
                foreach (var p in f.Pecas)
                {
                    var padrao = novo.Buscar(p.Codigo);
                    if (padrao != null && p.NomeBlocoDwg == $"P-{p.Comprimento}X14X19")
                    {
                        p.NomeBlocoDwg = padrao.NomeBlocoDwg;
                        p.Layer = padrao.Layer;
                        p.Cor = padrao.Cor;
                    }
                }
                // Canaletas padrão antigas (nomes supostos) saem; voltam na fase das elevações.
                f.Pecas.RemoveAll(p => p.Funcao == FuncaoPeca.Canaleta && p.NomeBlocoDwg.StartsWith("P-CAN-", StringComparison.Ordinal));
            }
            catalogo.Familias.RemoveAll(f => f.Nome == "CERAMICO-39" &&
                                             f.Pecas.All(p => p.NomeBlocoDwg.StartsWith("P-CER-", StringComparison.Ordinal)));
        }
    }
}
