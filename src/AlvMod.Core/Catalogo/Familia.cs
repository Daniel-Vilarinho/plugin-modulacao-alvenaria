using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace AlvMod.Core.Catalogo
{
    public enum Material
    {
        Concreto,
        Ceramico
    }

    [DataContract]
    public sealed class Familia
    {
        [DataMember(Name = "nome", Order = 1)] public string Nome { get; set; }

        public Material Material { get; set; }

        [DataMember(Name = "material", Order = 2)]
        private string MaterialTexto
        {
            get => Material.ToString();
            set => Material = Enum.TryParse(value, true, out Material m) ? m : Material.Concreto;
        }

        /// <summary>Espessura da junta vertical, em cm.</summary>
        [DataMember(Name = "junta", Order = 3)] public int Junta { get; set; } = 1;

        [DataMember(Name = "pecas", Order = 4)] public List<Peca> Pecas { get; set; } = new List<Peca>();

        [OnDeserializing]
        private void AntesDeLer(StreamingContext _)
        {
            Junta = 1;
            Pecas = new List<Peca>();
        }

        /// <summary>Peças que o motor pode usar numa fiada comum (habilitadas, sem canaletas).</summary>
        public IEnumerable<Peca> PecasDeFiada() =>
            Pecas.Where(p => p.Habilitada && p.Funcao != FuncaoPeca.Canaleta);

        public Peca Buscar(string codigo) =>
            Pecas.FirstOrDefault(p => string.Equals(p.Codigo, codigo, StringComparison.OrdinalIgnoreCase));

        public Peca BuscarPorBloco(string nomeBloco) =>
            Pecas.FirstOrDefault(p => string.Equals(p.NomeBlocoDwg, nomeBloco, StringComparison.OrdinalIgnoreCase));

        /// <summary>Cópia com as peças indicadas desabilitadas (peças que a construtora não usa nesta obra).</summary>
        public Familia ParaObra(IEnumerable<string> pecasDesabilitadas)
        {
            var fora = new HashSet<string>(pecasDesabilitadas ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var copia = (Familia)MemberwiseClone();
            copia.Pecas = Pecas.Select(p =>
            {
                var c = p.Clonar();
                if (fora.Contains(c.Codigo)) c.Habilitada = false;
                return c;
            }).ToList();
            return copia;
        }

        public override string ToString() => Nome;
    }
}
