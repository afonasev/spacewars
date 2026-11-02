"""Validate mandatory native dependencies before binding or packaging a Player."""
import pathlib
import struct

WINDOWS_RECAST = pathlib.Path('Player_Data/Plugins/x86_64/spacewars_recast.dll')
RECAST_EXPORTS = {
    'sw_recast_abi', 'sw_recast_layer_abi', 'sw_recast_memory_abi',
    'sw_recast_memory_scope', 'sw_recast_build', 'sw_recast_build_layered',
    'sw_recast_map_free', 'sw_recast_query', 'sw_recast_step',
    'sw_recast_copy', 'sw_recast_copy_areas', 'sw_recast_copy_portals',
    'sw_recast_query_free', 'sw_recast_live_maps', 'sw_recast_live_queries',
    'sw_recast_allocation_count', 'sw_recast_allocation_bytes',
}


def validate_windows_recast(path):
    path = pathlib.Path(path)
    if not path.is_file():
        raise ValueError('Required Windows Recast DLL missing: ' + str(path))
    data = path.read_bytes()
    try:
        pe = struct.unpack_from('<I', data, 0x3c)[0]
        if data[:2] != b'MZ' or data[pe:pe+4] != b'PE\0\0':
            raise ValueError('Invalid Windows Recast PE header')
        machine, count = struct.unpack_from('<HH', data, pe+4)
        optional_size, flags = struct.unpack_from('<HH', data, pe+20)
        optional = pe+24
        if machine != 0x8664 or not flags & 0x2000 or struct.unpack_from('<H', data, optional)[0] != 0x20b:
            raise ValueError('Windows Recast must be an x64 DLL')
        sections = optional+optional_size

        def offset(rva):
            for i in range(count):
                size, address, raw_size, start = struct.unpack_from('<IIII', data, sections+i*40+8)
                if address <= rva < address+min(size, raw_size):
                    return start+rva-address
            raise ValueError('Invalid Windows Recast RVA')

        def string(rva):
            start = offset(rva)
            return data[start:data.index(b'\0', start)].decode('ascii')

        export_rva = struct.unpack_from('<I', data, optional+112)[0]
        exports = offset(export_rva)
        names_count = struct.unpack_from('<I', data, exports+24)[0]
        names_rva = struct.unpack_from('<I', data, exports+32)[0]
        names = offset(names_rva)
        actual = {string(struct.unpack_from('<I', data, names+i*4)[0]) for i in range(names_count)}
        if not RECAST_EXPORTS <= actual:
            raise ValueError('Windows Recast ABI exports missing: ' + ', '.join(sorted(RECAST_EXPORTS-actual)))
        # The distributed plugin must not require a separate MinGW runtime install.
        import_rva = struct.unpack_from('<I', data, optional+120)[0]
        if import_rva:
            at = offset(import_rva)
            while any(data[at:at+20]):
                name = string(struct.unpack_from('<I', data, at+12)[0]).lower()
                if name.startswith(('libgcc', 'libstdc++', 'libwinpthread')):
                    raise ValueError('Windows Recast requires an unbundled compiler runtime: ' + name)
                at += 20
        return path
    except (struct.error, IndexError, UnicodeError) as error:
        raise ValueError('Truncated Windows Recast DLL') from error
