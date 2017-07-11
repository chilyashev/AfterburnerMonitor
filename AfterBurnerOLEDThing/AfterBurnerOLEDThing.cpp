/*
MIT License

Copyright (c) 2017 Mihail Chilyashev <m@chilyashev.com>

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/
#include "stdafx.h"
#include "RTSSSharedMemory.h"
#include "MAHMSharedMemory.h"
#include "AfterBurnerOLEDThing.h"
#include <iostream>

#include <shlwapi.h>
#include <float.h>
#include <io.h>

void Connect();
void Disconnect();

HANDLE			m_hMapFile;
LPVOID			m_pMapAddr;
REPORT			m_stReport;
CString			m_strInstallPath;

/////////////////////////////////////////////////////////////////////////////
// This function is used to connect to MSI Afterburner hardware monitoring
// shared memory
/////////////////////////////////////////////////////////////////////////////
void Connect()
{
	Disconnect();
	//we must disconnect from the previously connected shared memory before
	//connecting to new one

	m_hMapFile = OpenFileMapping(FILE_MAP_ALL_ACCESS, FALSE, "MAHMSharedMemory");

	if (m_hMapFile) {
		m_pMapAddr = MapViewOfFile(m_hMapFile, FILE_MAP_ALL_ACCESS, 0, 0, 0);
	}
}
/////////////////////////////////////////////////////////////////////////////
// This function is used to disconnect from MSI Afterburner hardware monitoring
// shared memory
/////////////////////////////////////////////////////////////////////////////
void Disconnect()
{
	if (m_pMapAddr) {
		UnmapViewOfFile(m_pMapAddr);
	}

	m_pMapAddr = NULL;

	if (m_hMapFile) {
		CloseHandle(m_hMapFile);
	}

	m_hMapFile = NULL;
}



extern "C" {
	__declspec(dllexport) const int __cdecl  GetAfterburnerData(char* pszBuf, int nMaxLen)
	{
		CString strPacket; // Used to build the data packet

		memset(pszBuf, 0, nMaxLen);
		if (m_strInstallPath.IsEmpty())
		{
			HKEY hKey;

			if (ERROR_SUCCESS == RegOpenKey(HKEY_LOCAL_MACHINE, "Software\\MSI\\Afterburner", &hKey))
			{
				char buf[MAX_PATH];

				DWORD dwSize = MAX_PATH;
				DWORD dwType;

				if (ERROR_SUCCESS == RegQueryValueEx(hKey, "InstallPath", 0, &dwType, (LPBYTE)buf, &dwSize))
				{
					if (dwType == REG_SZ)
						m_strInstallPath = buf;
				}

				RegCloseKey(hKey);
			}
		}

		// Validate MSI Afterburner installation path

		if (_taccess(m_strInstallPath, 0))
			m_strInstallPath = "";


		if (!m_pMapAddr)
			Connect();

		if (m_pMapAddr)
			//if we're connected to shared memory, we must check if it is valid or not and reconnect if necessary
		{
			LPMAHM_SHARED_MEMORY_HEADER lpHeader = (LPMAHM_SHARED_MEMORY_HEADER)m_pMapAddr;

			if (lpHeader->dwSignature == 0xDEAD)
				//if the memory is marked as dead (e.g. MSI Afterburner was unloaded), we should disconnect from it and
				//try to connect again
				Connect();
		}


		if (m_pMapAddr)
		{
			LPMAHM_SHARED_MEMORY_HEADER	lpHeader = (LPMAHM_SHARED_MEMORY_HEADER)m_pMapAddr;

			if (lpHeader->dwSignature == 'MAHM')
				//check if we're connected to valid memory
			{
				CTime time(lpHeader->time);
				CString strBuf = time.Format("%d.%m.%Y %H:%M:%S");
				m_stReport.strTime = strBuf;
				// Format data polling time
				DWORD dwSources = lpHeader->dwNumEntries;
				// Get number of data sources

				if (lpHeader->dwVersion >= 0x00020000)
					// GPU entries are available only in v2.0 and newer shared memory format
				{
					// Get number of GPUs
					DWORD dwGpus = lpHeader->dwNumGpuEntries;

					for (DWORD dwGpu = 0; dwGpu < dwGpus; dwGpu++)
						// Get info for each GPU
					{
						LPMAHM_SHARED_MEMORY_GPU_ENTRY	lpGpuEntry = (LPMAHM_SHARED_MEMORY_GPU_ENTRY)((LPBYTE)lpHeader + lpHeader->dwHeaderSize + lpHeader->dwNumEntries * lpHeader->dwEntrySize + dwGpu * lpHeader->dwGpuEntrySize);
						//get ptr to the current GPU entry

						for (DWORD dwSource = 0; dwSource < dwSources; dwSource++)
							//display info for each data source
						{
							LPMAHM_SHARED_MEMORY_ENTRY	lpEntry = (LPMAHM_SHARED_MEMORY_ENTRY)((LPBYTE)lpHeader + lpHeader->dwHeaderSize + dwSource * lpHeader->dwEntrySize);
							//get ptr to the current data source entry

							if (lpEntry->dwGpu != dwGpu) {
								// Filter data source entries by GPU index
								continue;
							}

							// Get GPU temp
							if (lpEntry->dwSrcId == MONITORING_SOURCE_ID_GPU_TEMPERATURE) {
								m_stReport.fGpu = lpEntry->data;
							}
						}

					}

					// Additional pass for global data sources (having GPU index set to 0xFFFFFFFF)

					for (DWORD dwSource = 0; dwSource < dwSources; dwSource++)
						//display info for each data source
					{
						LPMAHM_SHARED_MEMORY_ENTRY	lpEntry = (LPMAHM_SHARED_MEMORY_ENTRY)((LPBYTE)lpHeader + lpHeader->dwHeaderSize + dwSource * lpHeader->dwEntrySize);
						//get ptr to the current data source entry

						if (lpEntry->dwGpu != 0xFFFFFFFF) {
							// Filter data source entries by GPU index
							continue;
						}

						// Get FPS
						if (lpEntry->dwSrcId == MONITORING_SOURCE_ID_FRAMERATE) {
							m_stReport.fFPS = lpEntry->data;
						}

						// Get CPU temp
						if (lpEntry->dwSrcId == MONITORING_SOURCE_ID_CPU_TEMPERATURE) {
							m_stReport.fCpu = lpEntry->data;
						}

					}

				}

			}
			else {
				return ERR_UNITIALIZED_MEMORY; // Connected to uninitialized MSI Afterburner Hardware Monitoring shared memory
			}

		}
		else
		{
			if (m_strInstallPath.IsEmpty()) {
				return ERR_AFTERBURNER_NOT_INSTALLED;// Failed to connect to MSI Afterburner Hardware Monitoring shared memory! Hints: Install MSI Afterburner
			}
			else {
				return ERR_AFTERBURNER_NOT_RUNNING;// Failed to connect to MSI Afterburner Hardware Monitoring shared memory! Hints: Start MSI Afterburner";
			}
		}

		// Collect all the data in a big pile and return it.
		strPacket.Format("%.2f;%.2f;%.2f;", m_stReport.fCpu, m_stReport.fGpu, m_stReport.fFPS);

		lstrcpyn(pszBuf, strPacket.GetBuffer(), nMaxLen);

		return strPacket.GetLength();
	}
}