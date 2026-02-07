#ifndef SHARED_EMULATED_SPI_H
#define SHARED_EMULATED_SPI_H

#include "datatypes.h"
#include <stddef.h>
#include <string.h>

void spi_read(SPI_Address_t address, size_t size, uint8_t buf[]);

#endif // SHARED_EMULATED_SPI_H
