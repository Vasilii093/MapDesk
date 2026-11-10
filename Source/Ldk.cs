using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

namespace AlpineQuestPort {
 // Read-only LDK v1 container, current waypoint/set payloads. Never follows external links.
 public sealed class LdkReader {
  sealed class Reader {
   public byte[] Data;public int Position;
   public Reader(byte[] data,int position=0){Data=data;Position=position;}
   public void Need(int count){if(count<0||Position<0||count>Data.Length-Position)throw new InvalidDataException("Обрезанные данные LDK.");}
   public int Int(){Need(4);uint v=0;for(int i=0;i<4;i++)v=(v<<8)|Data[Position++];return unchecked((int)v);}
   public long Long(){return ((long)Int()<<32)|(uint)Int();}
   public byte Byte(){Need(1);return Data[Position++];}
   public byte[] Bytes(int n){Need(n);var result=new byte[n];Buffer.BlockCopy(Data,Position,result,0,n);Position+=n;return result;}
   public string Text(int n=-1){if(n==-1)n=Int();if(n<0||n>1048576)throw new InvalidDataException("Размер строки LDK превышен.");return new UTF8Encoding(false,true).GetString(Bytes(n));}
   public void Skip(int n){Need(n);Position+=n;}
  }
  readonly byte[] data;readonly HashSet<long> visited=new HashSet<long>();readonly MapDocument document=new MapDocument();long readBudget;
  LdkReader(byte[] bytes){data=bytes;}
  static void Check(bool ok){if(!ok)throw new InvalidDataException("Повреждённая или неподдерживаемая структура LDK.");}
  Reader At(long offset){Check(offset>=0&&offset<=data.Length-4);return new Reader(data,(int)offset);}
  byte[] Block(long offset){var r=At(offset);Check(r.Int()==0x105555);r.Int();long length=r.Long(),capacity=r.Long(),next=r.Long();Check(length>=0&&length<=32*1024*1024&&capacity>=0);readBudget+=length;Check(readBudget<=128L*1024*1024);var output=new MemoryStream();var seen=new HashSet<long>();seen.Add(offset);
   while(true){int amount=(int)Math.Min(length-output.Length,capacity);var part=r.Bytes(amount);output.Write(part,0,part.Length);if(output.Length==length)break;Check(next>0&&seen.Add(next));r=At(next);Check(r.Int()==0x205555);capacity=r.Long();next=r.Long();Check(capacity>0);}
   return output.ToArray();
  }
  static Dictionary<string,object> Metadata(Reader r,bool modern,bool extended,int depth=0){Check(depth<12);int count=r.Int();Check(count>=-1&&count<=10000);var result=new Dictionary<string,object>();if(count<0)return result;
   for(int i=0;i<count;i++){string key=r.Text();int kind=r.Int();object value=null;if(kind>=0)value=r.Text(kind);else switch(kind){case -1:value=r.Byte()!=0;break;case -2:value=r.Long();break;case -3:value=BitConverter.Int64BitsToDouble(r.Long());break;case -4:int size=r.Int();Check(size>=0&&size<=1048576);r.Skip(size);break;case -99:break;default:throw new InvalidDataException("Неизвестный тип метаданных LDK.");}result[key]=value;}
   if(modern)r.Int();if(extended){int children=r.Int();Check(children>=-1&&children<=10000);for(int i=0;i<children;i++){r.Text();Metadata(r,true,false,depth+1);}}return result;
  }
  static string Text(Dictionary<string,object> metadata,string key){object value;return metadata.TryGetValue(key,out value)&&value is string?(string)value:"";}
  static int ColorValue(Dictionary<string,object> metadata,int fallback){string value=Text(metadata,"color");if(value.Length==0)return fallback;try{var color=ColorTranslator.FromHtml(value);return color.ToArgb()&0xffffff;}catch{return fallback;}}
  static string PathGroup(string parent,string name){return String.IsNullOrWhiteSpace(name)?parent:String.IsNullOrEmpty(parent)?name:parent+" / "+name;}
  void Point(Reader r,string group,int inherited){var metadata=Metadata(r,true,true);int length=r.Int();Check(length>=8&&length<=1024);r.Need(length);int end=r.Position+length;double lon=r.Int()/1e7,lat=r.Int()/1e7;Check(GeoMath.IsValidCoordinate(lat,lon));var point=new GeoPoint(lat,lon){Name=Text(metadata,"name"),Group=group,ColorRgb=ColorValue(metadata,inherited)};
   while(r.Position<end){int tag=r.Byte();switch(tag){case 101:int elevation=r.Int();if(elevation!=-999999999)point.Elevation=elevation/1000.0;break;case 116:long time=r.Long();if(time!=0)point.Time=new DateTimeOffset(1970,1,1,0,0,0,TimeSpan.Zero).AddMilliseconds(time);break;case 97:case 112:case 118:r.Skip(4);break;case 98:r.Skip(1);break;case 110:r.Skip(2);break;case 115:r.Skip(8);break;default:r.Position=end;break;}Check(r.Position<=end);}
   if(String.IsNullOrWhiteSpace(point.Name))point.Name="Точка "+(document.Waypoints.Count+1);Check(document.Waypoints.Count<200000);document.Waypoints.Add(point);
  }
  void Payload(long offset,string group,int color){var r=new Reader(Block(offset));int type=r.Byte(),signature=r.Int();if(type==101&&signature==0x50500a01){int header=r.Int();r.Skip(header);Point(r,group,color);}else if(type==102&&signature==0x50500b01){int header=r.Int();Check(header>=0);int end=r.Position+header;r.Need(header);Metadata(r,true,true);var metadata=Metadata(r,true,true);Check(r.Position==end);group=PathGroup(group,Text(metadata,"name"));color=ColorValue(metadata,color);int count=r.Int();Check(count>=0&&count<=200000-document.Waypoints.Count);for(int i=0;i<count;i++)Point(r,group,color);}else throw new NotSupportedException("Этот LDK содержит неподдерживаемый тип или версию объекта. Сейчас доступны отдельные точки и наборы точек современного формата; для треков используйте GPX/KML.");Check(r.Position==r.Data.Length);}
  void Node(long offset,string parent,int inherited,int depth){Check(depth<64&&visited.Count<10000&&visited.Add(offset));var r=At(offset);Check(r.Int()==0x15555);r.Int();long metadataOffset=r.Long(),entries=r.Long();var metadata=metadataOffset==0?new Dictionary<string,object>():Metadata(new Reader(Block(metadataOffset)),false,false);string group=PathGroup(parent,Text(metadata,"name"));int color=ColorValue(metadata,inherited);var lists=new HashSet<long>();
   while(entries!=0){Check(lists.Add(entries)&&lists.Count<10000);r=At(entries);int tag=r.Int(),capacity=r.Int(),nodes=r.Int(),items;long next;int table;
    if(tag==0x25555){items=r.Int();next=r.Long();table=r.Position;Check(capacity>=0&&nodes>=0&&items>=0&&nodes+items<=capacity);}else if(tag==0x45555){items=nodes;nodes=capacity;capacity=nodes+items;next=0;table=r.Position;Check(nodes>=0&&items>=0);}else throw new InvalidDataException("Неизвестная таблица LDK.");
    Check(capacity<=200000);r.Need(capacity*12);
    for(int i=0;i<nodes;i++){var slot=new Reader(data,table+i*12);long child=slot.Long();if(child>0)Node(child,group,color,depth+1);}
    for(int i=0;i<items;i++){int slotIndex=tag==0x25555?capacity-1-i:nodes+i;var slot=new Reader(data,table+slotIndex*12);long child=slot.Long();if(child>0)Payload(child,group,color);}entries=next;
   }
  }
  public static MapDocument Load(string path){var info=new FileInfo(path);Check(info.Exists&&info.Length>=48&&info.Length<=64L*1024*1024);var loader=new LdkReader(File.ReadAllBytes(path));var r=new Reader(loader.data);Check(r.Int()==0x4c444b3a&&r.Int()==1);long root=r.Long();loader.Node(root,Path.GetFileNameWithoutExtension(path),0x176B50,0);return loader.document;}
 }
}
