using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace AlvMod.Plugin.Desenho
{
    /// <summary>Trecho reto do eixo de uma parede, em unidades do desenho.</summary>
    internal sealed class Trecho
    {
        public Point3d Inicio { get; }
        public Point3d Fim { get; }
        public Vector3d Direcao { get; }
        public Vector3d Perpendicular { get; }
        public double Comprimento { get; }

        public Trecho(Point3d inicio, Point3d fim)
        {
            Inicio = inicio;
            Fim = fim;
            Comprimento = inicio.DistanceTo(fim);
            Direcao = (fim - inicio).GetNormal();
            Perpendicular = new Vector3d(-Direcao.Y, Direcao.X, 0);
        }

        /// <summary>Posição ao longo do eixo (t) e afastamento lateral (d) de um ponto.</summary>
        public void Projetar(Point3d p, out double t, out double d)
        {
            var v = p - Inicio;
            t = v.DotProduct(Direcao);
            d = v.DotProduct(Perpendicular);
        }
    }

    internal static class Geometria
    {
        /// <summary>Pede ao usuário um LINE ou um segmento reto de LWPOLYLINE (o mais próximo do clique).</summary>
        public static Trecho SelecionarTrecho(Editor ed, Transaction tr, string mensagem)
        {
            var opcoes = new PromptEntityOptions(mensagem);
            opcoes.SetRejectMessage("\nSelecione uma LINE ou POLYLINE.");
            opcoes.AddAllowedClass(typeof(Line), true);
            opcoes.AddAllowedClass(typeof(Polyline), true);

            var res = ed.GetEntity(opcoes);
            if (res.Status != PromptStatus.OK) return null;

            var ent = (Entity)tr.GetObject(res.ObjectId, OpenMode.ForRead);
            if (ent is Line linha)
                return new Trecho(linha.StartPoint, linha.EndPoint);

            var pl = (Polyline)ent;
            var clique = res.PickedPoint.TransformBy(ed.CurrentUserCoordinateSystem);
            var maisProximo = pl.GetClosestPointTo(clique, Vector3d.ZAxis, false);
            int segmentos = pl.Closed ? pl.NumberOfVertices : pl.NumberOfVertices - 1;
            int i = Math.Min(Math.Max((int)Math.Floor(pl.GetParameterAtPoint(maisProximo)), 0), segmentos - 1);

            if (pl.GetSegmentType(i) != SegmentType.Line)
            {
                ed.WriteMessage("\nO segmento selecionado é um arco. Só paredes retas são suportadas por enquanto.");
                return null;
            }
            var seg = pl.GetLineSegmentAt(i);
            if (segmentos > 1)
                ed.WriteMessage($"\nPolilinha com {segmentos} segmentos: usando o segmento {i + 1} (encontros ainda não são tratados).");
            return new Trecho(seg.StartPoint, seg.EndPoint);
        }

        /// <summary>Cantos de uma peça desenhada (retângulo POLYLINE ou bloco DWG), em coordenadas do desenho.</summary>
        public static List<Point3d> Cantos(Entity ent, Transaction tr)
        {
            var pontos = new List<Point3d>();
            if (ent is Polyline pl)
            {
                // Só retângulos fechados: ignora o próprio eixo e outras polilinhas que vierem na seleção.
                if (!pl.Closed || pl.NumberOfVertices != 4) return pontos;
                for (int i = 0; i < pl.NumberOfVertices; i++) pontos.Add(pl.GetPoint3dAt(i));
            }
            else if (ent is BlockReference br)
            {
                // Extensão da definição do bloco, transformada pela inserção (funciona em parede inclinada).
                var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                var ext = new Extents3d();
                bool vazio = true;
                foreach (ObjectId id in btr)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity filho) || filho is AttributeDefinition) continue;
                    try
                    {
                        if (vazio) { ext = filho.GeometricExtents; vazio = false; }
                        else ext.AddExtents(filho.GeometricExtents);
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception) { /* entidade sem extensão */ }
                }
                if (vazio) return pontos;

                var min = ext.MinPoint;
                var max = ext.MaxPoint;
                foreach (var p in new[]
                         {
                             new Point3d(min.X, min.Y, 0), new Point3d(max.X, min.Y, 0),
                             new Point3d(max.X, max.Y, 0), new Point3d(min.X, max.Y, 0)
                         })
                    pontos.Add(p.TransformBy(br.BlockTransform));
            }
            return pontos;
        }
    }
}
