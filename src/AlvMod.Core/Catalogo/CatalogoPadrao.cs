using System.Collections.Generic;

namespace AlvMod.Core.Catalogo
{
    /// <summary>
    /// Catálogo inicial, gravado no arquivo de catálogo na primeira execução. Depois disso o usuário
    /// edita pelo comando ALVCATALOGO (ou direto no JSON).
    /// Peças conforme o questionário (item 11). Nomes de bloco seguem a convenção do item 53
    /// ("P-39X14X19" em planta). Layers, cores e custos são valores iniciais ajustáveis.
    /// </summary>
    public static class CatalogoPadrao
    {
        public const string Concreto39 = "CONCRETO-39";
        public const string Ceramico39 = "CERAMICO-39";

        public static CatalogoBlocos Criar() => new CatalogoBlocos
        {
            Familias = new List<Familia> { Familia39Concreto(), Familia39Ceramico() }
        };

        public static Familia Familia39Concreto() => new Familia
        {
            Nome = Concreto39,
            Material = Material.Concreto,
            Junta = 1,
            Pecas = new List<Peca>
            {
                Nova("39", 39, FuncaoPeca.Principal, "P-39X14X19", "BLOCO 14X39", 10, cor: 3),
                Nova("19", 19, FuncaoPeca.Meio, "P-19X14X19", "BLOCO 14X19", 12, cor: 4),
                Nova("54", 54, FuncaoPeca.Encontro, "P-54X14X19", "BLOCO 14X54", 14, cor: 6,
                    habilitada: false), // só em encontros de parede (fase de encontros L/T/X)
                Nova("34", 34, FuncaoPeca.Amarracao, "P-34X14X19", "BLOCO 14X34", 20, cor: 5),
                Nova("9", 9, FuncaoPeca.Compensador, "P-9X14X19", "COMPENSADOR 14X9", 22, cor: 1, grauteavel: false),
                Nova("4", 4, FuncaoPeca.Compensador, "P-4X14X19", "COMPENSADOR 14X4", 24, cor: 2, grauteavel: false),
                Nova("C19", 19, FuncaoPeca.Canaleta, "P-CAN-19X14X19", "CANALETA 14X19", 0, cor: 30),
                Nova("C34", 34, FuncaoPeca.Canaleta, "P-CAN-34X14X19", "CANALETA 14X34", 0, cor: 30),
                Nova("C39", 39, FuncaoPeca.Canaleta, "P-CAN-39X14X19", "CANALETA 14X39", 0, cor: 30),
            }
        };

        public static Familia Familia39Ceramico() => new Familia
        {
            Nome = Ceramico39,
            Material = Material.Ceramico,
            Junta = 1,
            Pecas = new List<Peca>
            {
                Nova("39", 39, FuncaoPeca.Principal, "P-CER-39X14X19", "CERAMICO 14X39", 10, cor: 3),
                Nova("19", 19, FuncaoPeca.Meio, "P-CER-19X14X19", "CERAMICO 14X19", 12, cor: 4),
                Nova("4", 4, FuncaoPeca.Compensador, "P-CER-4X14X19", "CERAMICO 14X4", 24, cor: 2, grauteavel: false),
            }
        };

        private static Peca Nova(string codigo, int comprimento, FuncaoPeca funcao, string bloco, string layer,
            int custo, short cor, bool grauteavel = true, bool habilitada = true) => new Peca
        {
            Codigo = codigo,
            Comprimento = comprimento,
            Largura = 14,
            Altura = 19,
            Funcao = funcao,
            NomeBlocoDwg = bloco,
            Layer = layer,
            Cor = cor,
            Custo = custo,
            Grauteavel = grauteavel,
            Habilitada = habilitada
        };
    }
}
