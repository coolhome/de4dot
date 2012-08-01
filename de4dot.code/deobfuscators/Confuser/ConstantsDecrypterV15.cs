﻿/*
    Copyright (C) 2011-2012 de4dot@gmail.com

    This file is part of de4dot.

    de4dot is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    de4dot is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with de4dot.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Confuser {
	class ConstantsDecrypterV15 {
		ModuleDefinition module;
		ISimpleDeobfuscator simpleDeobfuscator;
		MethodDefinition decryptMethod;
		EmbeddedResource resource;
		uint key0, key1, key2, key3;
		byte doubleType, singleType, int32Type, int64Type, stringType;
		BinaryReader reader;

		public MethodDefinition Method {
			get { return decryptMethod; }
		}

		public EmbeddedResource Resource {
			get { return resource; }
		}

		public bool Detected {
			get { return decryptMethod != null; }
		}

		public ConstantsDecrypterV15(ModuleDefinition module, ISimpleDeobfuscator simpleDeobfuscator) {
			this.module = module;
			this.simpleDeobfuscator = simpleDeobfuscator;
		}

		static readonly string[] requiredLocals = new string[] {
			"System.Byte[]",
			"System.Collections.Generic.Dictionary`2<System.UInt32,System.Object>",
			"System.IO.BinaryReader",
			"System.IO.Compression.DeflateStream",
			"System.IO.MemoryStream",
			"System.Random",
			"System.Reflection.Assembly",
		};
		public void find() {
			var type = DotNetUtils.getModuleType(module);
			if (type == null)
				return;
			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				if (!DotNetUtils.isMethod(method, "System.Object", "(System.UInt32)"))
					continue;
				var localTypes = new LocalTypes(method);
				if (!localTypes.all(requiredLocals))
					continue;

				decryptMethod = method;
				break;
			}
		}

		public void initialize() {
			if ((resource = findResource(decryptMethod)) == null)
				throw new ApplicationException("Could not find encrypted consts resource");

			if (!initializeKeys())
				throw new ApplicationException("Could not find all keys");
			if (!initializeTypeCodes())
				throw new ApplicationException("Could not find all type codes");

			var constants = DeobUtils.inflate(resource.GetResourceData(), true);
			reader = new BinaryReader(new MemoryStream(constants));
		}

		bool initializeKeys() {
			if (!findKey0(decryptMethod, out key0))
				return false;
			if (!findKey1(decryptMethod, out key1))
				return false;
			if (!findKey2Key3(decryptMethod, out key2, out key3))
				return false;

			return true;
		}

		static bool findKey0(MethodDefinition method, out uint key) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 5; i++) {
				if (!DotNetUtils.isLdloc(instrs[i]))
					continue;
				if (instrs[i + 1].OpCode.Code != Code.Or)
					continue;
				var ldci4 = instrs[i + 2];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;
				if (instrs[i + 3].OpCode.Code != Code.Xor)
					continue;
				if (instrs[i + 4].OpCode.Code != Code.Add)
					continue;
				if (!DotNetUtils.isStloc(instrs[i + 5]))
					continue;

				key = (uint)DotNetUtils.getLdcI4Value(ldci4);
				return true;
			}
			key = 0;
			return false;
		}

		static bool findKey1(MethodDefinition method, out uint key) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				int index = ConfuserUtils.findCallMethod(instrs, i, Code.Callvirt, "System.Int32 System.Reflection.MemberInfo::get_MetadataToken()");
				if (index < 0)
					break;
				if (index + 2 > instrs.Count)
					break;
				if (!DotNetUtils.isStloc(instrs[index + 1]))
					continue;
				var ldci4 = instrs[index + 2];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;

				key = (uint)DotNetUtils.getLdcI4Value(ldci4);
				return true;
			}
			key = 0;
			return false;
		}

		static bool findKey2Key3(MethodDefinition method, out uint key2, out uint key3) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 3; i++) {
				var ldci4_1 = instrs[i];
				if (!DotNetUtils.isLdcI4(ldci4_1))
					continue;
				if (!DotNetUtils.isStloc(instrs[i + 1]))
					continue;
				var ldci4_2 = instrs[i + 2];
				if (!DotNetUtils.isLdcI4(ldci4_2))
					continue;
				if (!DotNetUtils.isStloc(instrs[i + 3]))
					continue;

				key2 = (uint)DotNetUtils.getLdcI4Value(ldci4_1);
				key3 = (uint)DotNetUtils.getLdcI4Value(ldci4_2);
				return true;
			}
			key2 = 0;
			key3 = 0;
			return false;
		}

		bool initializeTypeCodes() {
			var allBlocks = new Blocks(decryptMethod).MethodBlocks.getAllBlocks();
			if (!findTypeCode(allBlocks, out doubleType, Code.Call, "System.Double System.BitConverter::ToDouble(System.Byte[],System.Int32)"))
				return false;
			if (!findTypeCode(allBlocks, out singleType, Code.Call, "System.Single System.BitConverter::ToSingle(System.Byte[],System.Int32)"))
				return false;
			if (!findTypeCode(allBlocks, out int32Type, Code.Call, "System.Int32 System.BitConverter::ToInt32(System.Byte[],System.Int32)"))
				return false;
			if (!findTypeCode(allBlocks, out int64Type, Code.Call, "System.Int64 System.BitConverter::ToInt64(System.Byte[],System.Int32)"))
				return false;
			if (!findTypeCode(allBlocks, out stringType, Code.Callvirt, "System.String System.Text.Encoding::GetString(System.Byte[])"))
				return false;
			return true;
		}

		static bool findTypeCode(IList<Block> allBlocks, out byte typeCode, Code callCode, string bitConverterMethod) {
			foreach (var block in allBlocks) {
				if (block.Sources.Count != 1)
					continue;
				int index = ConfuserUtils.findCallMethod(block.Instructions, 0, callCode, bitConverterMethod);
				if (index < 0)
					continue;

				if (!findTypeCode(block.Sources[0], out typeCode))
					continue;

				return true;
			}
			typeCode = 0;
			return false;
		}

		static bool findTypeCode(Block block, out byte typeCode) {
			var instrs = block.Instructions;
			int numCeq = 0;
			for (int i = instrs.Count - 1; i >= 0; i--) {
				var instr = instrs[i];
				if (instr.OpCode.Code == Code.Ceq) {
					numCeq++;
					continue;
				}
				if (!DotNetUtils.isLdcI4(instr.Instruction))
					continue;
				if (numCeq != 0 && numCeq != 2)
					continue;

				typeCode = (byte)DotNetUtils.getLdcI4Value(instr.Instruction);
				return true;
			}
			typeCode = 0;
			return false;
		}

		EmbeddedResource findResource(MethodDefinition method) {
			return DotNetUtils.getResource(module, DotNetUtils.getCodeStrings(method)) as EmbeddedResource;
		}

		public object decryptInt32(MethodDefinition caller, uint magic) {
			byte typeCode;
			var data = decryptData(caller, magic, out typeCode);
			if (typeCode != int32Type)
				return null;
			if (data.Length != 4)
				throw new ApplicationException("Invalid data length");
			return BitConverter.ToInt32(data, 0);
		}

		public object decryptInt64(MethodDefinition caller, uint magic) {
			byte typeCode;
			var data = decryptData(caller, magic, out typeCode);
			if (typeCode != int64Type)
				return null;
			if (data.Length != 8)
				throw new ApplicationException("Invalid data length");
			return BitConverter.ToInt64(data, 0);
		}

		public object decryptSingle(MethodDefinition caller, uint magic) {
			byte typeCode;
			var data = decryptData(caller, magic, out typeCode);
			if (typeCode != singleType)
				return null;
			if (data.Length != 4)
				throw new ApplicationException("Invalid data length");
			return BitConverter.ToSingle(data, 0);
		}

		public object decryptDouble(MethodDefinition caller, uint magic) {
			byte typeCode;
			var data = decryptData(caller, magic, out typeCode);
			if (typeCode != doubleType)
				return null;
			if (data.Length != 8)
				throw new ApplicationException("Invalid data length");
			return BitConverter.ToDouble(data, 0);
		}

		public string decryptString(MethodDefinition caller, uint magic) {
			byte typeCode;
			var data = decryptData(caller, magic, out typeCode);
			if (typeCode != stringType)
				return null;
			return Encoding.UTF8.GetString(data);
		}

		byte[] decryptData(MethodDefinition caller, uint magic, out byte typeCode) {
			uint offs = calcHash(caller.MetadataToken.ToUInt32()) ^ magic;
			reader.BaseStream.Position = offs;
			typeCode = reader.ReadByte();
			if (typeCode != int32Type && typeCode != int64Type &&
				typeCode != singleType && typeCode != doubleType &&
				typeCode != stringType)
				throw new ApplicationException("Invalid type code");

			var encrypted = reader.ReadBytes(reader.ReadInt32());
			var rand = new Random((int)(key0 ^ offs));
			var decrypted = new byte[encrypted.Length];
			rand.NextBytes(decrypted);
			for (int i = 0; i < decrypted.Length; i++)
				decrypted[i] ^= encrypted[i];

			return decrypted;

		}

		uint calcHash(uint x) {
			uint h0 = key1 ^ x;
			uint h1 = key2;
			uint h2 = key3;
			for (uint i = 1; i <= 64; i++) {
				h0 = (h0 << 8) | (h0 >> 24);
				uint n = h0 & 0x3F;
				if (n >= 0 && n < 16) {
					h1 |= ((byte)(h0 >> 8) & (h0 >> 16)) ^ (byte)~h0;
					h2 ^= (h0 * i + 1) & 0xF;
					h0 += (h1 | h2) ^ key0;
				}
				else if (n >= 16 && n < 32) {
					h1 ^= ((h0 & 0x00FF00FF) << 8) ^ (ushort)((h0 >> 8) | ~h0);
					h2 += (h0 * i) & 0x1F;
					h0 |= (h1 + ~h2) & key0;
				}
				else if (n >= 32 && n < 48) {
					h1 += (byte)(h0 | (h0 >> 16)) + (~h0 & 0xFF);
					h2 -= ~(h0 + n) % 48;
					h0 ^= (h1 % h2) | key0;
				}
				else if (n >= 48 && n < 64) {
					h1 ^= ((byte)(h0 >> 16) | ~(h0 & 0xFF)) * (~h0 & 0x00FF0000);
					h2 += (h0 ^ (i - 1)) % n;
					h0 -= ~(h1 ^ h2) + key0;
				}
			}
			return h0;
		}
	}
}
