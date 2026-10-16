using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace AlvMod.Core.Catalogo
{
    /// <summary>Todas as famílias cadastradas. Fica num arquivo JSON por computador.</summary>
    [DataContract]
    public sealed class CatalogoBlocos
    {
        [DataMember(Name = "familias", Order = 1)] public List<Familia> Familias { get; set; } = new List<Familia>();

        [OnDeserializing]
        private void AntesDeLer(StreamingContext _) => Familias = new List<Familia>();

        public Familia Buscar(string nome) =>
            Familias.FirstOrDefault(f => string.Equals(f.Nome, nome, StringComparison.OrdinalIgnoreCase));

        /// <summary>Peça dona de um bloco DWG em qualquer família.</summary>
        public Peca BuscarPorBloco(string nomeBloco) =>
            Familias.Select(f => f.BuscarPorBloco(nomeBloco)).FirstOrDefault(p => p != null);

        public static CatalogoBlocos Carregar(string caminho)
        {
            if (!File.Exists(caminho))
            {
                var padrao = CatalogoPadrao.Criar();
                padrao.Salvar(caminho);
                return padrao;
            }
            return Json.Ler<CatalogoBlocos>(File.ReadAllBytes(caminho));
        }

        public void Salvar(string caminho)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(caminho)));
            File.WriteAllBytes(caminho, Json.Escrever(this));
        }
    }

    /// <summary>JSON com indentação, legível e editável à mão.</summary>
    public static class Json
    {
        public static byte[] Escrever<T>(T objeto)
        {
            using (var ms = new MemoryStream())
            {
                using (var w = JsonReaderWriterFactory.CreateJsonWriter(ms, new UTF8Encoding(false), false, true, "  "))
                    new DataContractJsonSerializer(typeof(T)).WriteObject(w, objeto);
                return ms.ToArray();
            }
        }

        public static T Ler<T>(byte[] json)
        {
            using (var ms = new MemoryStream(json))
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(ms);
        }

        public static string EscreverTexto<T>(T objeto) => Encoding.UTF8.GetString(Escrever(objeto));

        public static T LerTexto<T>(string json) => Ler<T>(Encoding.UTF8.GetBytes(json));
    }
}
