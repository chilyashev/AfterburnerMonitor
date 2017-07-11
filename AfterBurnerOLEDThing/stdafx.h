// stdafx.h : include file for standard system include files,
// or project specific include files that are used frequently, but
// are changed infrequently
//

#pragma once

#include "targetver.h"
#define _ATL_CSTRING_EXPLICIT_CONSTRUCTORS      // some CString constructors will be explicit


#define _USE_32BIT_TIME_T	// Force 32-bit time_t usage to provide compatibility with VC8.0

#include <stdio.h>
#include <tchar.h>

#include <afxwin.h>         // MFC core and standard components
//#include <afxext.h>         // MFC extensions


#include <afxdisp.h>        // MFC Automation classes

typedef struct _REPORT
{
	float fCpu;
	float fGpu;
	float fFPS;
	CString strTime;

} REPORT;