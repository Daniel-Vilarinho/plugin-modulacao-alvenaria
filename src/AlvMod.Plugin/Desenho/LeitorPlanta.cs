using System;
using System.Collections.Generic;
using AlvMod.Core.Planta;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AlvMod.Plugin.Desenho
{
    /// <summary>
    /// Lê linhas e textos da seleção, entrando nos blocos (a arquitetura costuma vir dentro de um bloco),
    /// e converte para cm. Entidades no layer "0" dentro de bloco herdam o layer do bloco.
    /// </summary>
    internal sealed class LeitorPlanta
    {
        private const int ProfundidadeMaxima = 6;
        private readonly double _fator;

        public List<Segmento> Segmentos { get; } = new List<Segmento>();
        public List<Rotulo> Rotulos { get; } = new List<Rotulo>();

        /// <summary>Blocos inseridos encontrados (nome, referência no desenho) — usados para ler a 1ª fiada.</summary>
        public List<BlockReference> Blocos { get; } = new List<BlockReference>();

        public LeitorPlanta(double fatorCm) => _fator = fatorCm;

        public void Ler(Transaction tr, IEnumerable<ObjectId> ids)
        {
            foreach (var id in ids)
                if (tr.GetObject(id, OpenMode.ForRead) is Entity e)
                    Ler(e, null, 0, tr);
        }

        private void Ler(Entity e, string layerPai, int nivel, Transaction tr)
        {
            string layer = layerPai != null && e.Layer == "0" ? layerPai : e.Layer;
            switch (e)
            {
                case Line l:
                    Adicionar(layer, l.StartPoint, l.EndPoint);
                    break;
                case Polyline pl:
                    int n = pl.Closed ? pl.NumberOfVertices : pl.NumberOfVertices - 1;
                    for (int i = 0; i < n; i++)
                    {
                        if (pl.GetSegmentType(i) == SegmentType.Arc)
                        {
                            var arco = pl.GetArcSegmentAt(i);
                            var meio = arco.EvaluatePoint((arco.GetParameterOf(arco.StartPoint) + arco.GetParameterOf(arco.EndPoint)) / 2);
                            Adicionar(layer, arco.StartPoint, meio);
                            Adicionar(layer, meio, arco.EndPoint);
                        }
                        else
                        {
                            var s = pl.GetLineSegmentAt(i);
                            Adicionar(layer, s.StartPoint, s.EndPoint);
                        }
                    }
                    break;
                case Arc a:
                    var pm = a.GetPointAtParameter((a.StartParam + a.EndParam) / 2);
                    Adicionar(layer, a.StartPoint, pm);
                    Adicionar(layer, pm, a.EndPoint);
                    break;
                case DBText t:
                    Rotulos.Add(new Rotulo(t.TextString, t.Position.X * _fator, t.Position.Y * _fator));
                    break;
                case MText mt:
                    Rotulos.Add(new Rotulo(mt.Text, mt.Location.X * _fator, mt.Location.Y * _fator));
                    break;
                case BlockReference br:
                    if (nivel == 0 && br.Database != null) Blocos.Add(br);
                    if (nivel >= ProfundidadeMaxima) break;
                    var partes = new DBObjectCollection();
                    try
                    {
                        br.Explode(partes); // cópias já transformadas para as coordenadas do desenho
                        foreach (DBObject o in partes)
                            if (o is Entity filho) Ler(filho, layer, nivel + 1, tr);
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception)
                    {
                        // Blocos que não explodem (ex.: escala não uniforme em alguns casos) são ignorados.
                    }
                    finally
                    {
                        foreach (DBObject o in partes) o.Dispose();
                    }
                    break;
            }
        }

        private void Adicionar(string layer, Point3d a, Point3d b) =>
            Segmentos.Add(new Segmento(layer, a.X * _fator, a.Y * _fator, b.X * _fator, b.Y * _fator));
    }
}
