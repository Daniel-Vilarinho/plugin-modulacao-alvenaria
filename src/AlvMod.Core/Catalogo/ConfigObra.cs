using System.Collections.Generic;
using System.Runtime.Serialization;

namespace AlvMod.Core.Catalogo
{
    /// <summary>O que existe ao lado de um trecho de parede (comandos parede por parede).</summary>
    public enum TipoAbertura
    {
        /// <summary>Encosta em outra parede (canto, T): a parede continua ali.</summary>
        Parede,
        Porta,
        Janela,
        /// <summary>Vão sem esquadria (não soma folga de marco).</summary>
        Passagem,
        /// <summary>Ponta de parede solta, sem continuidade.</summary>
        Livre
    }

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

        /// <summary>Folga de marco somada ao vão da arquitetura nas portas (cm, total dos dois lados).</summary>
        [DataMember(Name = "folgaPorta", Order = 7)] public double FolgaPorta { get; set; } = 11;

        /// <summary>Folga de marco somada ao vão da arquitetura nas janelas (cm, total dos dois lados).</summary>
        [DataMember(Name = "folgaJanela", Order = 8)] public double FolgaJanela { get; set; } = 6;

        /// <summary>Layers da arquitetura lidos pelo ALVPLANTA (escolhidos clicando numa linha de cada tipo).</summary>
        [DataMember(Name = "layersParede", Order = 9)] public List<string> LayersParede { get; set; } = new List<string>();
        [DataMember(Name = "layersPorta", Order = 10)] public List<string> LayersPorta { get; set; } = new List<string>();
        [DataMember(Name = "layersJanela", Order = 11)] public List<string> LayersJanela { get; set; } = new List<string>();

        [OnDeserializing]
        private void AntesDeLer(StreamingContext _)
        {
            LayersParede = new List<string>();
            LayersPorta = new List<string>();
            LayersJanela = new List<string>();
            PecasDesabilitadas = new List<string>();
            FatorCm = 1;
            Escala = 50;
            Precos = new Dictionary<string, double>();
            FolgaPorta = 11;
            FolgaJanela = 6;
        }

        public bool Configurada => !string.IsNullOrEmpty(Familia);
    }
}
