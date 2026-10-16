namespace AlvMod.Core.Catalogo
{
    public enum FuncaoPeca
    {
        Principal,
        Amarracao,
        Meio,
        Encontro,
        Compensador,
        Canaleta
    }

    /// <summary>Peça (bloco de alvenaria) de uma família. Medidas em cm.</summary>
    public sealed class Peca
    {
        public string Codigo { get; set; }
        public int Comprimento { get; set; }
        public int Largura { get; set; }
        public FuncaoPeca Funcao { get; set; }

        /// <summary>Nome do bloco DWG (BlockReference) usado para desenhar a peça.</summary>
        public string NomeBlocoDwg { get; set; }

        public string Layer { get; set; }
        public bool Grauteavel { get; set; } = true;

        /// <summary>Peças desabilitadas na obra nunca são usadas pelo motor.</summary>
        public bool Habilitada { get; set; } = true;

        /// <summary>
        /// Custo relativo usado pelo motor para escolher entre composições que fecham.
        /// Menor = preferida. Valores provisórios: precisam ser calibrados pelo engenheiro.
        /// </summary>
        public int Custo { get; set; }

        public override string ToString() => Codigo;
    }
}
