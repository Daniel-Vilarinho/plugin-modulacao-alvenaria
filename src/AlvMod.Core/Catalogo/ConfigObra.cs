using System.Collections.Generic;
using System.Runtime.Serialization;

namespace AlvMod.Core.Catalogo
{
    /// <summary>
    /// Configuração da obra, gravada DENTRO do DWG: quem abrir o arquivo em outra máquina usa a mesma.
    /// </summary>
    [DataContract]
    public sealed class ConfigObra
    {
        [DataMember(Name = "familia", Order = 1)] public string Familia { get; set; }

        /// <summary>Códigos das peças que a construtora não usa nesta obra.</summary>
        [DataMember(Name = "pecasDesabilitadas", Order = 2)] public List<string> PecasDesabilitadas { get; set; } = new List<string>();

        /// <summary>Quantos cm vale 1 unidade do desenho.</summary>
        [DataMember(Name = "fatorCm", Order = 3)] public double FatorCm { get; set; } = 1;

        /// <summary>Denominador da escala de impressão (50 para 1:50). Define o tamanho das cotas.</summary>
        [DataMember(Name = "escala", Order = 4)] public double Escala { get; set; } = 50;

        /// <summary>Deslocamento (unidades do desenho) da planta da 1ª fiada até a planta da 2ª fiada.</summary>
        [DataMember(Name = "deslocamento2F", Order = 5)] public double[] Deslocamento2F { get; set; }

        /// <summary>Preço unitário por código de peça, quando a construtora informa.</summary>
        [DataMember(Name = "precos", Order = 6)] public Dictionary<string, double> Precos { get; set; } = new Dictionary<string, double>();

        [OnDeserializing]
        private void AntesDeLer(StreamingContext _)
        {
            PecasDesabilitadas = new List<string>();
            FatorCm = 1;
            Escala = 50;
            Precos = new Dictionary<string, double>();
        }

        public bool Configurada => !string.IsNullOrEmpty(Familia);
    }
}
